using Google.Cloud.Vision.V1;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using WebDriverManager.Helpers;
using WinClipboard = System.Windows.Forms.Clipboard;
using SKeys = OpenQA.Selenium.Keys;

namespace Tennis
{
	public class Account
	{
		public string ID = "";
		public string PW = "";
		public string SPW = "";
	}

	public class ReservationData
	{
		public Account Acc = null!;
		public string Court = "";
		public string CourtNumber = "";
		public int Year;
		public int Month;
		public int Day;
		public int StartTime;
		public int EndTime;
		public bool IsDelay;
		public int StartDelay = 0;
	}

	public class ReservationThread : IDisposable
	{
		// 로그인 상태 확인용 홈 URL / 로그인 폼 URL
		private const string HomeUrl = "https://www.naver.com/";
		private const string LoginUrl =
			"https://nid.naver.com/nidlogin.login?mode=form&url=https://www.naver.com/";

		public ReservationData data;
		private IWebDriver driver = null!;        // Init() 에서 설정
		private SeleniumHelper selenium = null!;  // Init() 에서 설정
		private ChromeDriverService? driverService;
		private Process? chromeProcess;
		private System.Threading.Timer timer = null!;  // 미사용 상태지만 레거시 필드 유지
		private string _curUrl = "";
		private bool _disposed = false;

		public Action<string>? LogAction;
		public int ThreadNo;

		// 멀티스레드 동시 실행 시 ChromeDriver 셋업/바이너리 패치 충돌 방지
		private static readonly object _driverSetupLock = new object();

		public ReservationThread(ReservationData _data)
		{
			data = _data;
		}

		/// <summary>
		/// 영구 Chrome 프로필 폴더 (계정별로 분리).
		/// %LOCALAPPDATA%\TennisBot\profile-{ID}
		/// 첫 로그인 성공 후 쿠키/디바이스ID 가 저장되어 다음 실행부터 "신뢰 기기"로 인식됨.
		/// </summary>
		private string UserDataDir =>
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"TennisBot",
				$"profile-{SanitizeFileName(data.Acc.ID)}");

		/// <summary>스레드별로 다른 디버그 포트 사용 (멀티스레드 동시 실행 대비).</summary>
		private int DebugPort => 9222 + ThreadNo;

		private void Log(string msg)
		{
			LogAction?.Invoke($"[{ThreadNo}] {msg}");
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;

			// 1) CDP 연결(driver) 끊기 — 공식 Quit 은 attach 모드에서 Chrome 전체를 끌 수 있어 Dispose 만.
			try { driver?.Dispose(); } catch { }

			// 2) 이 스레드가 띄운 Chrome 프로세스 종료 (프로세스 트리 전체 Kill)
			try
			{
				if (chromeProcess != null && !chromeProcess.HasExited)
				{
					try { chromeProcess.CloseMainWindow(); } catch { }
					if (!chromeProcess.WaitForExit(2000))
						chromeProcess.Kill(entireProcessTree: true);
					chromeProcess.WaitForExit(3000);
				}
			}
			catch { }
			finally
			{
				try { chromeProcess?.Dispose(); } catch { }
				chromeProcess = null;
			}

			// 3) chromedriver.exe 종료 — DriverService 를 Dispose 하면 자식 chromedriver 가 함께 정리됨.
			try
			{
				if (driverService != null)
				{
					int pid = 0;
					try { pid = driverService.ProcessId; } catch { }

					try { driverService.Dispose(); } catch { }

					if (pid > 0)
					{
						try
						{
							var p = Process.GetProcessById(pid);
							if (!p.HasExited) p.Kill(entireProcessTree: true);
						}
						catch { /* 이미 종료됨 */ }
					}
				}
			}
			catch { }
			finally
			{
				driverService = null;
			}
		}

		public void DoStart()
		{
			try
			{
				Thread.Sleep(data.StartDelay);
				Init();
				// 성공 경로: 결제 창이 열려있어야 사용자가 2차 비번 등 수동 결제 완료 가능.
				// Dispose 는 폼 종료/정지 버튼으로만 호출되도록 두고 여기서는 더 이상 창을 닫지 않는다.
				Log("자동화 완료 — 결제 창은 열어둠 (수동 결제 또는 정지 버튼으로 종료)");
			}
			catch (Exception e)
			{
				try { Log($"오류 발생: {e.Message}"); } catch { }
				// 실패 경로도 창을 바로 닫으면 디버깅이 어려우므로 그대로 두고,
				// Dispose 는 사용자의 명시적 정지(OnClose) 또는 폼 종료 시에만 실행.
			}
		}

		public void Init()
		{
			// 1) chromedriver.exe 준비 + $cdc_ 자동화 시그니처 패치
			string driverPath;
			lock (_driverSetupLock)
			{
				driverPath = new DriverManager().SetUpDriver(new ChromeConfig(), VersionResolveStrategy.MatchingBrowser);
				PatchChromeDriver(driverPath);
			}

			var service = ChromeDriverService.CreateDefaultService(Path.GetDirectoryName(driverPath));
			service.HideCommandPromptWindow = true;
			driverService = service;

			// 2) Attach 모드로 Chrome.exe 를 직접 실행하고 CDP 로 접속
			//    (--enable-automation 등 자동화 플래그가 Chrome 에 전혀 들어가지 않음)
			InitDriverAttach(service);
			selenium = new SeleniumHelper(driver) { LogAction = LogAction };

			// 3) 추가 가드: navigator.webdriver 등 숨김
			PatchNavigator();

			Login();
			Thread.Sleep(1000);

			FindCourt();
			Thread.Sleep(1000);

			ProcessReservation();
		}

		/// <summary>
		/// Chrome.exe 를 직접 실행하고 Selenium 은 Remote Debugging 포트로 접속만.
		/// </summary>
		private void InitDriverAttach(ChromeDriverService service)
		{
			Directory.CreateDirectory(UserDataDir);
			Log($"Chrome 프로필: {UserDataDir}");
			Log($"디버그 포트: {DebugPort}");

			// 0) orphan 정리: 이전 세션에서 비정상 종료되어 포트/프로필을 잡고 있는
			//    Chrome 프로세스가 있으면 kill 한다. 안 그러면 "포트는 열려있지만 CDP 응답 없음"
			//    상태로 ChromeDriver.attach 가 멈춤.
			KillStaleChromeForThisInstance();
			ClearProfileSingletonLocks();

			// 1) 포트가 아직도 열려있으면 (= 우리가 못 죽인 외부 프로세스가 잡고 있음) 경고.
			if (IsPortOpen("127.0.0.1", DebugPort))
				Log($"[WARN] 포트 {DebugPort} 가 orphan 정리 후에도 열려있음 — 다른 프로그램이 선점했을 수 있음");

			var chromePath = FindChromeExecutable()
				?? throw new FileNotFoundException("Chrome.exe 를 찾을 수 없습니다.");

			var args =
				$"--remote-debugging-port={DebugPort} " +
				$"--user-data-dir=\"{UserDataDir}\" " +
				"--no-first-run --no-default-browser-check --lang=ko-KR " +
				"--start-maximized";

			Log($"Chrome 실행: port={DebugPort}");
			var psi = new ProcessStartInfo(chromePath, args)
			{
				UseShellExecute = false,
				CreateNoWindow = false,
			};
			chromeProcess = Process.Start(psi);

			// 2) 디버그 포트가 열릴 때까지 대기 (최대 15초)
			var deadline = DateTime.UtcNow.AddSeconds(15);
			while (DateTime.UtcNow < deadline)
			{
				if (IsPortOpen("127.0.0.1", DebugPort)) break;
				Thread.Sleep(200);
			}
			if (!IsPortOpen("127.0.0.1", DebugPort))
				throw new TimeoutException($"Chrome 디버그 포트({DebugPort}) 가 열리지 않았습니다.");
			Log("디버그 포트 열림 확인");

			// 3) ChromeDriver attach — 응답 없으면 여기서 멈춤. 로그로 멈춤 위치 추적 가능.
			Log("ChromeDriver attach 시도...");
			var options = new ChromeOptions
			{
				DebuggerAddress = $"127.0.0.1:{DebugPort}"
			};
			driver = new ChromeDriver(service, options);
			Log("ChromeDriver attach 성공");
		}

		/// <summary>
		/// 이 스레드의 포트/프로필 경로를 쓰는 orphan chrome.exe 프로세스를 정리한다.
		/// PowerShell + CIM 쿼리로 command line 을 조회해서 매칭되는 것만 kill (EncodedCommand 로 escape 회피).
		/// </summary>
		private void KillStaleChromeForThisInstance()
		{
			try
			{
				var udd = UserDataDir;
				var port = DebugPort;
				// PowerShell 명령 — port 또는 user-data-dir 매칭 시 Stop-Process
				var ps =
					"try {" +
					"  Get-CimInstance Win32_Process -Filter \"Name='chrome.exe'\" | Where-Object {" +
					"    $_.CommandLine -and (" +
					$"      $_.CommandLine -match 'remote-debugging-port={port}\\b' -or " +
					$"      $_.CommandLine -match [regex]::Escape('{udd}')" +
					"    )" +
					"  } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" +
					"} catch {}";

				var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(ps));
				var psi = new ProcessStartInfo("powershell.exe",
					$"-NoProfile -NonInteractive -EncodedCommand {encoded}")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				};
				using var p = Process.Start(psi);
				if (p != null)
				{
					if (!p.WaitForExit(5000))
					{
						try { p.Kill(entireProcessTree: true); } catch { }
					}
				}
				Log("orphan Chrome 정리 완료");
			}
			catch (Exception ex)
			{
				Log($"orphan 정리 중 예외(무시): {ex.Message}");
			}
		}

		/// <summary>
		/// Chrome 프로필 디렉토리의 Singleton* 락 파일(심볼릭 링크/일반 파일) 을 제거한다.
		/// Chrome 이 비정상 종료되면 이 락이 남아 다음 실행이 "이미 열려있음" 으로 판단되어 실패.
		/// </summary>
		private void ClearProfileSingletonLocks()
		{
			try
			{
				foreach (var name in new[] { "SingletonLock", "SingletonCookie", "SingletonSocket" })
				{
					var path = Path.Combine(UserDataDir, name);
					try
					{
						if (File.Exists(path) || Directory.Exists(path))
						{
							var attr = File.GetAttributes(path);
							// 심볼릭 링크(ReparsePoint) 처리
							if ((attr & FileAttributes.ReparsePoint) != 0)
								File.Delete(path);  // Windows 심링크도 Delete 로 제거됨
							else
								File.Delete(path);
						}
					}
					catch { /* 이미 없거나 못 지워도 치명적 아님 */ }
				}
			}
			catch { }
		}

		public void Login()
		{
			// 1) 홈에서 로그인 상태 체크 (쿠키 재사용 성공 시 로그인 과정 생략)
			Log("naver.com 접속 → 로그인 상태 확인");
			driver.Navigate().GoToUrl(HomeUrl);
			Wait();
			RandomSleep(500, 1000);

			if (IsLoggedIn())
			{
				Log("[SKIP] 이미 로그인 상태입니다. (쿠키 재사용 성공)");
				return;
			}

			// 2) 로그인 안 됨 → 로그인 폼으로 이동
			Log("로그인 안 됨 → 로그인 페이지로 이동");
			driver.Navigate().GoToUrl(LoginUrl);
			Wait();

			// 3) 봇 감지 회피: 클립보드 복사 → Ctrl+V 로 ID/PW 입력
			Log("ID 입력");
			FocusAndPaste("id", data.Acc.ID);
			RandomSleep(300, 700);

			Log("PW 입력");
			FocusAndPaste("pw", data.Acc.PW);
			RandomSleep(300, 700);

			// 4) '로그인 상태 유지' 체크 → NID_AUT 영구 쿠키 발급 → 다음 실행에 로그인 생략
			TryCheckKeepLogin();

			Log("로그인 버튼 클릭");
			selenium.Click("//*[@id=\"loginBtn_row\"]/span");

			// 5) 결과 대기 (캡차/2차인증은 사용자가 Chrome 창에서 직접 처리)
			WaitForLoginResult(TimeSpan.FromMinutes(3));
		}

		/// <summary>NID_AUT 쿠키 유무로 로그인 여부 판정.</summary>
		private bool IsLoggedIn()
		{
			try
			{
				foreach (var c in driver.Manage().Cookies.AllCookies)
				{
					if (c.Name == "NID_AUT" && !string.IsNullOrEmpty(c.Value))
						return true;
				}
			}
			catch { }
			return false;
		}

		/// <summary>로그인 완료(= NID_AUT 발급)까지 대기. 캡차/2차인증 수동 처리 가능.</summary>
		private void WaitForLoginResult(TimeSpan timeout)
		{
			var deadline = DateTime.UtcNow + timeout;
			while (DateTime.UtcNow < deadline)
			{
				try
				{
					if (IsLoggedIn())
					{
						Log("[OK] 로그인 성공 (NID_AUT 쿠키 확인)");
						return;
					}

					var url = driver.Url ?? "";
					if (url.Contains("captcha") || (driver.PageSource ?? "").Contains("자동입력 방지"))
						Log("[WAIT] 캡차/추가 인증 감지 - Chrome 창에서 직접 처리해 주세요");
				}
				catch { }
				Thread.Sleep(1000);
			}
			Log("[TIMEOUT] 로그인 완료를 확인하지 못했습니다.");
		}

		/// <summary>'로그인 상태 유지' 체크박스 활성화.</summary>
		private void TryCheckKeepLogin()
		{
			try
			{
				var keep = driver.FindElement(By.Id("keep"));
				if (!keep.Selected)
				{
					try
					{
						driver.FindElement(By.CssSelector("label[for='keep']")).Click();
					}
					catch
					{
						((IJavaScriptExecutor)driver).ExecuteScript(
							"document.getElementById('keep').click();");
					}
					Log("'로그인 상태 유지' 체크");
				}
			}
			catch { /* 체크박스 없으면 무시 */ }
		}

		private static bool IsPortOpen(string host, int port)
		{
			try
			{
				using var client = new TcpClient();
				var ar = client.BeginConnect(host, port, null, null);
				if (!ar.AsyncWaitHandle.WaitOne(500)) return false;
				client.EndConnect(ar);
				return true;
			}
			catch { return false; }
		}

		private static string? FindChromeExecutable()
		{
			string[] candidates =
			{
				Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe"),
				Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"),
				Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"),
			};
			foreach (var p in candidates)
				if (File.Exists(p)) return p;
			return null;
		}

		private static string SanitizeFileName(string s)
		{
			foreach (var c in Path.GetInvalidFileNameChars())
				s = s.Replace(c, '_');
			return string.IsNullOrWhiteSpace(s) ? "default" : s;
		}

		/// <summary>
		/// 입력 필드에 포커스를 두고 클립보드로 값을 붙여넣는다.
		/// SendKeys 는 keydown/keypress 이벤트 패턴으로 봇 판정되므로 Ctrl+V 가 가장 자연스럽다.
		/// </summary>
		private void FocusAndPaste(string elementId, string value)
		{
			var elem = driver.FindElement(By.Id(elementId));

			elem.Click();
			RandomSleep(100, 250);

			elem.SendKeys(SKeys.Control + "a");
			RandomSleep(50, 150);
			elem.SendKeys(SKeys.Delete);
			RandomSleep(50, 150);

			SetClipboardText(value);
			RandomSleep(80, 180);

			new Actions(driver)
				.KeyDown(SKeys.Control)
				.SendKeys("v")
				.KeyUp(SKeys.Control)
				.Perform();
		}

		private static void SetClipboardText(string text)
		{
			try { WinClipboard.SetText(text); return; }
			catch { }

			// STA 가 아닌 경우 폴백
			var t = new Thread(() =>
			{
				try { WinClipboard.SetText(text); } catch { }
			});
			t.SetApartmentState(ApartmentState.STA);
			t.Start();
			t.Join();
		}

		private static readonly Random _rng = new Random();
		private static void RandomSleep(int minMs, int maxMs)
		{
			Thread.Sleep(_rng.Next(minMs, maxMs));
		}

		/// <summary>
		/// CDP 로 navigator.webdriver 등을 숨겨 자동화 감지를 회피한다.
		/// </summary>
		private void PatchNavigator()
		{
			try
			{
				if (driver is ChromeDriver cd)
				{
					var cmdParams = new Dictionary<string, object>
					{
						["source"] =
							"Object.defineProperty(navigator, 'webdriver', {get: () => undefined});" +
							"Object.defineProperty(navigator, 'languages', {get: () => ['ko-KR', 'ko', 'en-US', 'en']});" +
							"Object.defineProperty(navigator, 'plugins', {get: () => [1,2,3,4,5]});" +
							"window.chrome = { runtime: {} };"
					};
					cd.ExecuteCdpCommand("Page.addScriptToEvaluateOnNewDocument", cmdParams);
				}
			}
			catch (Exception ex)
			{
				Log($"CDP 패치 실패(무시 가능): {ex.Message}");
			}
		}

		public void FindCourt()
		{
			if (data.Court == "양재")
				FindYangJaeCourt();
			else
				FindNagokCourt();
		}

		public void FindYangJaeCourt()
		{
			var findName = $"{data.Month}월 {data.CourtNumber}코트";
			NavigateToCourtViaGraphQL("210031", findName);
		}

		public void FindNagokCourt()
		{
			// data.CourtNumber 는 이미 "번" 을 포함 (예: "1번") — 응답 name 은 "11월 내곡 1번코트(하드)"
			var findName = $"{data.Month}월 내곡 {data.CourtNumber}코트";
			NavigateToCourtViaGraphQL("217811", findName);
		}

		private void NavigateToCourtViaGraphQL(string businessId, string findName)
		{
			driver.Navigate().GoToUrl($"https://booking.naver.com/booking/10/bizes/{businessId}/items");
			Thread.Sleep(500);

			var payload = System.Text.Json.JsonSerializer.Serialize(new
			{
				operationName = "bizItems",
				variables = new
				{
					input = new
					{
						businessId = businessId,
						lang = "ko",
						projections = "RESOURCE,MIN_MAX_PRICE"
					}
				},
				query = "query bizItems($input: BizItemsParams) { bizItems(input: $input) { bizItemId name } }"
			});

			var excludeNov = data.Month == 1;
			var script = @"
var callback = arguments[arguments.length - 1];
var payload = arguments[0];
var findName = arguments[1];
var excludeNov = arguments[2];
fetch('/graphql?opName=bizItems', {
    method: 'POST',
    headers: {
        'Content-Type': 'application/json',
        'Accept': '*/*',
        'x-booking-client-device': 'pc',
        'x-booking-client-environment': 'pc_web',
        'x-booking-client-os': 'windows',
        'x-booking-is-naver-map-inapp-browser': 'false',
        'x-booking-is-pc-map': 'false'
    },
    body: payload,
    credentials: 'include'
})
.then(function(r){ return r.json(); })
.then(function(j){
    var items = (j && j.data && j.data.bizItems) || [];
    for (var i = 0; i < items.length; i++) {
        var it = items[i];
        if (!it.name || it.name.indexOf(findName) === -1) continue;
        if (excludeNov && it.name.indexOf('11월') !== -1) continue;
        callback(it.bizItemId);
        return;
    }
    callback('');
})
.catch(function(e){ callback('ERROR:' + e.message); });
";

			driver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(15);
			string bizItemId;
			try
			{
				bizItemId = ((IJavaScriptExecutor)driver).ExecuteAsyncScript(script, payload, findName, excludeNov) as string ?? "";
			}
			catch (Exception ex)
			{
				Log($"[{findName}] GraphQL 호출 실패: {ex.Message}");
				return;
			}

			if (string.IsNullOrEmpty(bizItemId) || bizItemId.StartsWith("ERROR:"))
			{
				Log($"[{findName}] bizItem 조회 실패: {bizItemId}");
				return;
			}

			var urlAdd = $"endDateTime={data.Year}-{data.Month:D2}-{data.Day:D2}T{data.EndTime:D2}%3A00%3A00%2B09%3A00&startDate={data.Year:D2}-{data.Month:D2}-{data.Day:D2}&startDateTime={data.Year}-{data.Month:D2}-{data.Day:D2}T{data.StartTime:D2}%3A00%3A00%2B09%3A00";
			var finalUrl = $"https://booking.naver.com/booking/10/bizes/{businessId}/items/{bizItemId}?{urlAdd}";
			_curUrl = finalUrl;
			driver.Navigate().GoToUrl(finalUrl);
		}

		public void OnClose()
		{
			Dispose();
		}

		public void WindowScrollBottom()
		{
			IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
			js.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
		}

		private bool CheckDate()
		{
			try
			{
				// JS 네이티브 click 디스패치 → 실패 시 WebDriver click 폴백.
				// router.push 로 URL 만 바꾸면 버튼 onClick 핸들러가 설정하는 React state 를 건너뛰어
				// /request/ 가 뒤로 튕기거나 다른 페이지로 보내짐 → 반드시 실제 click 을 거쳐야 함.
				var js = (IJavaScriptExecutor)driver;
				var script = @"
var btn = document.evaluate('//*[@id=""root""]/main/div[2]/div/button',
    document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
if (!btn) return 'NO_BTN';
var txt = (btn.innerText || btn.textContent || '').trim();
if (txt !== '다음') return 'NOT_NEXT:' + txt;
if (btn.disabled) return 'DISABLED';
btn.scrollIntoView({block:'center'});
btn.click();
return 'CLICKED';
";
				var result = js.ExecuteScript(script) as string ?? "";
				if (result == "CLICKED")
				{
					Log("OK (JS click)");
					var deadline = DateTime.UtcNow.AddSeconds(2);
					while (DateTime.UtcNow < deadline)
					{
						try
						{
							if ((driver.Url ?? "").Contains("/request"))
								return true;
						}
						catch { }
						Thread.Sleep(50);
					}
					return true;
				}

				if (result.StartsWith("NOT_NEXT") || result == "NO_BTN" || result == "DISABLED")
					return false;

				WindowScrollBottom();
				var btn2 = selenium.GetElem("//*[@id=\"root\"]/main/div[2]/div/button");
				if (btn2 != null && btn2.Text == "다음")
				{
					Log("OK (fallback click)");
					if (btn2.Displayed && btn2.Enabled)
						btn2.Click();
					return true;
				}
			}
			catch
			{
				Wait();
			}
			return false;
		}

		/// <summary>
		/// 동의 버튼 클릭 전에 window.fetch 를 감싸서 submitBooking 응답을 가로챈다.
		/// 응답 구조: data.submitBooking.url.pc = "https://orders.pay.naver.com/order/bridge/easybooking/{uuid}?..."
		/// 응답이 오자마자 window._bookingPayUrl 에 저장 → C# 쪽에서 폴링.
		/// 이로써 Thread.Sleep(5000) 를 제거하고 새 창 생성 대기 없이 메인 창을 바로 결제 URL 로 보낼 수 있다.
		/// </summary>
		private void InstallSubmitBookingHook()
		{
			try
			{
				var js = (IJavaScriptExecutor)driver;
				js.ExecuteScript(@"
if (!window._bookingHookInstalled) {
    window._bookingPayUrl = null;
    window._bookingPayError = null;
    window._bookingReqBody = null;       // 캡처된 요청 바디 (JSON 문자열)
    window._bookingResBody = null;       // 전체 응답 바디
    window._bookingFetchFired = false;   // fetch 가 실제로 쐈는지 (팝업으로 막혔는지 판별)
    window._bookingAlerts = [];          // 네이티브 alert/confirm 메시지
    window._bookingLog = [];             // 범용 로그

    // (1) fetch 후킹 — request / response 둘 다 저장
    var origFetch = window.fetch;
    window.fetch = function() {
        var urlStr = '';
        try {
            var u = arguments[0];
            urlStr = (typeof u === 'string') ? u : (u && u.url) || '';
        } catch (e) {}

        var isSubmit = urlStr.indexOf('submitBooking') >= 0;
        if (isSubmit) {
            window._bookingFetchFired = true;
            try {
                var body = (arguments[1] && arguments[1].body) || null;
                if (body) {
                    if (typeof body === 'string') window._bookingReqBody = body;
                    else if (body instanceof FormData) {
                        var kv = [];
                        body.forEach(function(v,k){ kv.push(k+'='+v); });
                        window._bookingReqBody = kv.join('&');
                    } else { window._bookingReqBody = JSON.stringify(body); }
                }
            } catch (e) { window._bookingLog.push('req capture err: ' + e.message); }
        }

        var p = origFetch.apply(this, arguments);
        if (isSubmit) {
            p.then(function(r){ return r.clone().text(); })
             .then(function(t){
                 try {
                     window._bookingResBody = t;
                     var d = JSON.parse(t);
                     if (d && d.errors && d.errors.length) {
                         window._bookingPayError = JSON.stringify(d.errors);
                         return;
                     }
                     var urlObj = d && d.data && d.data.submitBooking && d.data.submitBooking.url;
                     if (urlObj) window._bookingPayUrl = urlObj.pc || urlObj.mobile || null;
                 } catch (e) { window._bookingPayError = 'parse:' + (e && e.message); }
             })
             .catch(function(e){ window._bookingPayError = 'fetch:' + (e && e.message); });
        }
        return p;
    };

    // (2) alert/confirm/beforeunload 를 전부 통과시켜 JS 실행 멈춤 방지
    //     어떤 메시지가 떴는지는 _bookingAlerts 에 기록 → C# 쪽에서 로그
    try {
        var origAlert = window.alert;
        window.alert = function(msg){ try { window._bookingAlerts.push('alert:' + msg); } catch(e){} };
    } catch (e) {}
    try {
        var origConfirm = window.confirm;
        window.confirm = function(msg){ try { window._bookingAlerts.push('confirm:' + msg); } catch(e){} return true; };
    } catch (e) {}
    try {
        var origPrompt = window.prompt;
        window.prompt = function(msg, def){ try { window._bookingAlerts.push('prompt:' + msg); } catch(e){} return def || ''; };
    } catch (e) {}
    try { window.onbeforeunload = null; } catch (e) {}

    window._bookingHookInstalled = true;
}
return 'OK';
");
			}
			catch { /* 후킹 실패해도 기존 흐름(새 창 전환) 으로 동작하도록 */ }
		}

		/// <summary>캡처된 디버그 정보를 로그로 뿌린다. 성공/실패 상관없이 호출.</summary>
		private void DumpBookingHookDebug()
		{
			try
			{
				var js = (IJavaScriptExecutor)driver;
				var fetchFired = js.ExecuteScript("return !!window._bookingFetchFired;") as bool?;
				var alertsJson = js.ExecuteScript("return JSON.stringify(window._bookingAlerts || []);") as string ?? "[]";
				var reqBody = js.ExecuteScript("return window._bookingReqBody || '';") as string ?? "";
				var resBody = js.ExecuteScript("return window._bookingResBody || '';") as string ?? "";

				Log($"[진단] submitBooking fetch 발화: {(fetchFired == true ? "YES" : "NO")}");

				if (alertsJson != "[]")
					Log($"[진단] 네이티브 다이얼로그: {alertsJson}");

				if (!string.IsNullOrEmpty(reqBody))
				{
					// 바디가 길면 길이만 로그 (너무 길면 지라/로그 UI 가 깨짐)
					var preview = reqBody.Length > 500 ? reqBody.Substring(0, 500) + "...(truncated)" : reqBody;
					Log($"[진단] 요청 바디({reqBody.Length}B): {preview}");
					// 전체 바디는 파일로 저장 → 다음 단계에서 템플릿으로 재사용
					try
					{
						var path = Path.Combine(Path.GetTempPath(), $"submitBooking_req_{ThreadNo}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
						File.WriteAllText(path, reqBody);
						Log($"[진단] 요청 바디 저장: {path}");
					}
					catch { }
				}
				if (!string.IsNullOrEmpty(resBody))
				{
					var preview = resBody.Length > 500 ? resBody.Substring(0, 500) + "...(truncated)" : resBody;
					Log($"[진단] 응답 바디({resBody.Length}B): {preview}");
				}
			}
			catch (Exception ex)
			{
				Log($"[진단] 덤프 실패: {ex.Message}");
			}
		}

		/// <summary>
		/// 템플릿 JSON 을 로드해 세션별 가변 필드(csrfToken, 날짜/시간, hourBit, 가격 테이블, naverPayBackUrl)
		/// 만 덮어쓴 뒤 /graphql?opName=submitBooking 에 직접 POST 한다. 성공하면 결제 URL 을 payUrl 로 반환.
		/// 템플릿은 templates/submitBooking_{bizItemId}.json. 없으면 false 반환 → 호출측에서 버튼 클릭 폴백.
		/// </summary>
		private bool TrySubmitBookingDirect(out string payUrl)
		{
			payUrl = "";
			try
			{
				// 1) 현재 /request/ URL 에서 bizItemId 추출
				var curUrl = (((IJavaScriptExecutor)driver).ExecuteScript("return location.href;") as string) ?? "";
				var pathMatch = Regex.Match(curUrl, @"/items/(\d+)");
				if (!pathMatch.Success)
				{
					Log("[direct] URL 에서 bizItemId 추출 실패");
					return false;
				}
				var bizItemId = pathMatch.Groups[1].Value;

				// 2) 템플릿 로드
				var templatePath = Path.Combine(AppContext.BaseDirectory, "templates", $"submitBooking_{bizItemId}.json");
				if (!File.Exists(templatePath))
				{
					Log($"[direct] 템플릿 없음: {templatePath}");
					return false;
				}
				var templateJson = File.ReadAllText(templatePath);
				var root = Newtonsoft.Json.Linq.JObject.Parse(templateJson);
				var input = (Newtonsoft.Json.Linq.JObject)root["variables"]!["input"]!;

				// 3) csrfToken 추출 (__NEXT_DATA__ / Apollo cache / cookie 순서로 스캔)
				var csrfToken = ExtractCsrfTokenFromPage();
				if (string.IsNullOrEmpty(csrfToken))
				{
					Log("[direct] csrfToken 추출 실패 (페이지에서 찾을 수 없음)");
					return false;
				}
				input["csrfToken"] = csrfToken;

				// 4) 시간 필드 재계산 (KST → UTC)
				var dateStr = $"{data.Year:D4}-{data.Month:D2}-{data.Day:D2}";
				var startKst = new DateTime(data.Year, data.Month, data.Day, data.StartTime, 0, 0, DateTimeKind.Unspecified);
				var endKst = new DateTime(data.Year, data.Month, data.Day, data.EndTime, 0, 0, DateTimeKind.Unspecified);
				var startUtc = startKst.AddHours(-9);
				var endUtc = endKst.AddHours(-9);

				input["startDate"] = dateStr;
				input["endDate"] = dateStr;
				input["startDateTime"] = startUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
				input["endDateTime"] = endUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
				input["startMinute"] = null;
				input["endMinute"] = null;

				// hourBit: 24자 문자열, 예약 시간대의 비트를 1
				var hb = new char[24];
				for (int i = 0; i < 24; i++) hb[i] = (i >= data.StartTime && i < data.EndTime) ? '1' : '0';
				input["hourBit"] = new string(hb);

				// 5) 가격 테이블 — 시간당 템플릿 가격 * 예약 시간
				// 템플릿의 원래 시간당 단가를 bizItemDailyPriceJson 첫 값에서 추출
				int perHourPrice = 9000;
				try
				{
					var firstHourObj = input["bizItemDailyPriceJson"]?.FirstOrDefault() as Newtonsoft.Json.Linq.JObject;
					if (firstHourObj != null)
					{
						var firstVal = firstHourObj.Properties().FirstOrDefault()?.Value?.ToObject<int>();
						if (firstVal.HasValue) perHourPrice = firstVal.Value;
					}
				}
				catch { }

				int totalHours = data.EndTime - data.StartTime;
				var priceArray = new Newtonsoft.Json.Linq.JArray();
				for (int h = data.StartTime; h < data.EndTime; h++)
				{
					var key = $"{dateStr}T{h:D2}:00+09:00";
					priceArray.Add(new Newtonsoft.Json.Linq.JObject { [key] = perHourPrice });
				}
				input["bizItemDailyPriceJson"] = priceArray;
				input["price"] = perHourPrice * totalHours;
				input["bizItemPrice"] = perHourPrice * totalHours;

				// 6) naverPayBackUrl: 현재 /request/ URL 그대로
				input["naverPayBackUrl"] = curUrl;

				// 7) 전송
				var bodyStr = root.ToString(Newtonsoft.Json.Formatting.None);
				Log($"[direct] POST submitBooking (bodyLen={bodyStr.Length}, csrf={csrfToken.Substring(0, Math.Min(10, csrfToken.Length))}...)");

				var sendScript = @"
var cb = arguments[arguments.length - 1];
var body = arguments[0];
fetch('/graphql?opName=submitBooking', {
    method: 'POST',
    headers: {'Content-Type': 'application/json', 'Accept': '*/*'},
    credentials: 'include',
    body: body
})
.then(function(r){ return r.text(); })
.then(function(t){ cb(t); })
.catch(function(e){ cb('ERROR:' + (e && e.message)); });
";
				driver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(15);
				var result = ((IJavaScriptExecutor)driver).ExecuteAsyncScript(sendScript, bodyStr) as string ?? "";

				if (result.StartsWith("ERROR:"))
				{
					Log($"[direct] fetch 실패: {result}");
					return false;
				}

				var resp = Newtonsoft.Json.Linq.JObject.Parse(result);
				var errors = resp["errors"];
				if (errors != null)
				{
					var preview = resp.ToString(Newtonsoft.Json.Formatting.None);
					if (preview.Length > 400) preview = preview.Substring(0, 400) + "...";
					Log($"[direct] GraphQL errors: {preview}");
					return false;
				}

				var url = resp.SelectToken("data.submitBooking.url.pc")?.ToString();
				if (string.IsNullOrEmpty(url))
				{
					var preview = result.Length > 400 ? result.Substring(0, 400) + "..." : result;
					Log($"[direct] URL 못 뽑음: {preview}");
					return false;
				}

				payUrl = url;
				Log($"[direct] ★ 결제 URL (직접 POST): {url}");
				return true;
			}
			catch (Exception ex)
			{
				Log($"[direct] 예외: {ex.Message}");
				return false;
			}
		}

		private static int CountHourBits(string hb)
		{
			int c = 0;
			foreach (var ch in hb) if (ch == '1') c++;
			return c;
		}

		private string ExtractCsrfTokenFromPage()
		{
			try
			{
				var js = (IJavaScriptExecutor)driver;
				var script = @"
try {
    var out = '';
    var sources = [];
    if (window.__NEXT_DATA__) sources.push(JSON.stringify(window.__NEXT_DATA__));
    if (window.__APOLLO_STATE__) sources.push(JSON.stringify(window.__APOLLO_STATE__));
    try {
        if (window.__APOLLO_CLIENT__ && window.__APOLLO_CLIENT__.cache && window.__APOLLO_CLIENT__.cache.extract) {
            sources.push(JSON.stringify(window.__APOLLO_CLIENT__.cache.extract()));
        }
    } catch (e) {}
    // 쿠키도 체크
    sources.push(document.cookie);
    // 메타태그도
    var metas = document.querySelectorAll('meta[name*=csrf i], meta[name*=xsrf i]');
    for (var i = 0; i < metas.length; i++) sources.push(metas[i].getAttribute('content') || '');

    var re = /[""']?csrfToken[""']?\s*[:=]\s*[""']([^""']+)[""']/;
    for (var i = 0; i < sources.length; i++) {
        if (!sources[i]) continue;
        var m = sources[i].match(re);
        if (m) { out = m[1]; break; }
    }
    return out;
} catch (e) { return ''; }
";
				return (js.ExecuteScript(script) as string) ?? "";
			}
			catch { return ""; }
		}

		/// <summary>
		/// /request/ 페이지의 "동의" 버튼을 가능한 한 빨리 누른다.
		/// 네이버 측 서버 mutation 이 실제로 일어나는 지점(slot hold 10분 발급) 이라 지연이 치명적.
		/// 1) 버튼이 DOM 에 나타나고 disabled 가 풀릴 때까지 JS 로 짧은 간격 폴링 (최대 15초)
		/// 2) 활성화 즉시 네이티브 click 디스패치
		/// 3) 실패 시 WebDriver click 폴백
		/// </summary>
		private void ClickAgreeFast(string xpath)
		{
			var js = (IJavaScriptExecutor)driver;
			var pollScript = @"
var xp = arguments[0];
var btn = document.evaluate(xp, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
if (!btn) return 'NO_BTN';
if (btn.disabled) return 'DISABLED';
// 활성 상태 → 즉시 클릭 (scrollIntoView 로 뷰포트 밖이어도 가능)
try { btn.scrollIntoView({block:'center'}); } catch (e) {}
btn.click();
return 'CLICKED';
";

			var deadline = DateTime.UtcNow.AddSeconds(15);
			string last = "";
			while (DateTime.UtcNow < deadline)
			{
				try
				{
					var r = js.ExecuteScript(pollScript, xpath) as string ?? "";
					last = r;
					if (r == "CLICKED")
					{
						Log("동의 (JS click)");
						return;
					}
				}
				catch { }
				Thread.Sleep(30);
			}

			// JS 로 15초 안에 못 눌렀으면 WebDriver 로 한 번 더 시도
			Log($"동의 JS 실패({last}) → WebDriver fallback");
			try
			{
				selenium.Click(xpath);
				Wait();
			}
			catch { }
		}

		private bool HasEnableButton(bool isDelay)
		{
			if (isDelay == true) return false;

			try
			{
				var btn = driver.FindElement(By.ClassName("NextButton__disabled__a3P-t"));
				return true;
			}
			catch
			{
				return false;
			}
		}

		private void Refresh()
		{
			try
			{
				driver.Navigate().Refresh();
			}
			catch
			{
				Thread.Sleep(500);
			}
		}

		private void Refresh(string url)
		{
			try
			{
				driver.Navigate().GoToUrl(url);
			}
			catch
			{
				Wait();
			}
		}

		// 예약 오픈 시각까지 대기. 정각 N초 전부터는 새로고침 없이 정밀 대기 후 리턴.
		private const int RefreshIntervalSec = 60;   // 오픈까지 여유 있을 때 새로고침 주기

		// 네트워크 왕복 보정용 선발사 오프셋(ms). 양수면 서버 정각보다 그만큼 앞서 Refresh 를 쏨.
		// 보수적으로 0 유지. 로그로 실제 발사 시각 확인 후 필요시 50~150 사이 값으로 조정.
		private const int FireLeadMs = 0;

		// (서버 UTC) - (로컬 UTC) 오프셋. 양수면 서버가 로컬보다 빠름.
		// SyncServerTime 으로 측정 — 로컬 Windows 시계가 수 초 틀어져 있어도 서버 정각에 발사하기 위함.
		private long _serverTimeOffsetMs = 0;

		/// <summary>
		/// HTTP Date 응답 헤더에서 서버 시간을 가져와 로컬 시계와의 오프셋을 저장한다.
		/// 로컬 시계가 조금만 어긋나도 '먼저 들어온 요청이 slot 을 잡는' 레이스에서 뒤처지므로 필수.
		/// Date 헤더는 초 단위라 ±500ms 오차는 있지만, 수 초 단위의 시스템 드리프트 제거만으로도 충분.
		/// </summary>
		private static readonly System.Net.Http.HttpClient _timeSyncClient = new System.Net.Http.HttpClient
		{
			Timeout = TimeSpan.FromSeconds(3)
		};

		private void SyncServerTime(string probeUrl)
		{
			try
			{
				if (string.IsNullOrEmpty(probeUrl))
				{
					_serverTimeOffsetMs = 0;
					return;
				}

				using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, probeUrl);
				req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

				var tBefore = DateTime.UtcNow;
				using var res = _timeSyncClient.Send(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
				var tAfter = DateTime.UtcNow;

				var dateHdr = res.Headers.Date;
				if (!dateHdr.HasValue)
				{
					Log("[시간동기] Date 헤더 없음 - 로컬 시간 사용");
					_serverTimeOffsetMs = 0;
					return;
				}

				var serverUtc = dateHdr.Value.UtcDateTime;
				// 요청 왕복 중간 지점을 로컬 측정 기준으로 삼음 (RTT/2 보정)
				var rtt = tAfter - tBefore;
				var tMid = tBefore + TimeSpan.FromTicks(rtt.Ticks / 2);
				_serverTimeOffsetMs = (long)(serverUtc - tMid).TotalMilliseconds;

				Log($"[시간동기] 서버-로컬 오프셋 {_serverTimeOffsetMs:+#;-#;0}ms (RTT {rtt.TotalMilliseconds:F0}ms)");
			}
			catch (Exception ex)
			{
				Log($"[시간동기] 실패 - 로컬 시간 사용: {ex.Message}");
				_serverTimeOffsetMs = 0;
			}
		}

		private DateTime ServerNowKst(TimeZoneInfo kstZone)
		{
			var utc = DateTime.UtcNow.AddMilliseconds(_serverTimeOffsetMs);
			return TimeZoneInfo.ConvertTimeFromUtc(utc, kstZone);
		}

		public void DoDelay()
		{
			TimeZoneInfo kstZone = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");

			// 로컬 시계 드리프트 보정 — Phase 1 진입 전에 1회
			SyncServerTime(_curUrl);

			// targetHour를 최초 진입 시 한 번만 계산 - Phase 1/2에서 재계산하면
			// Refresh 지연 등으로 정각을 살짝 지났을 때 다음 정각으로 넘어가는 버그 발생
			DateTime now = ServerNowKst(kstZone);
			DateTime next = now.AddHours(1);
			DateTime targetHour = new DateTime(next.Year, next.Month, next.Day, next.Hour, 0, 0);
			DateTime fireAt = targetHour.AddMilliseconds(-FireLeadMs);

			Log($"[대기] 타겟 {targetHour:HH:mm:ss} KST / 현재 {now:HH:mm:ss.fff} / 리드 {FireLeadMs}ms");

			// Phase 1: 세션 유지용 60초 고정 간격 Refresh.
			// 간격을 짧게(가속) 하면 네이버가 봇으로 판정 → 다른 페이지로 리다이렉트시키고
			// 예약 페이지로 돌아올 수 없게 막는다. 따라서 오픈이 가까워져도 가속하지 않는다.
			while (true)
			{
				now = ServerNowKst(kstZone);
				TimeSpan remain = fireAt - now;

				if (remain.TotalMilliseconds <= 0)
				{
					Log($"[대기] 이미 타겟 지남 - 즉시 Refresh ({now:HH:mm:ss.fff})");
					Refresh(_curUrl);
					return;
				}

				if (remain.TotalSeconds <= 30) break;  // 30초 이하 → Phase 2

				Log($"[대기] 정각까지 {remain.Minutes:D2}:{remain.Seconds:D2} 남음 (세션 유지 Refresh, 다음 60s 후)");

				WindowScrollBottom();

				// 다음 Refresh 가 Phase 2 영역(30초 이내)으로 넘어가지 않도록 상한
				int maxSleepMs = (int)Math.Max(0, remain.TotalMilliseconds - 30_000);
				int sleepMs = Math.Min(60_000, maxSleepMs);
				if (sleepMs > 0) Thread.Sleep(sleepMs);
				Refresh(_curUrl);
			}

			// Phase 2: 정밀 대기 — 초 단위 카운트다운 → Thread.Sleep → 마지막 50ms 는 busy-spin.
			// Windows Thread.Sleep 은 ~15ms 지터가 있어 마지막 구간만 스핀으로 ms 미만 정밀도 확보.
			int lastLoggedSec = -1;
			while (true)
			{
				now = ServerNowKst(kstZone);
				double remainMs = (fireAt - now).TotalMilliseconds;

				if (remainMs <= 0) break;

				int secLeft = (int)Math.Ceiling(remainMs / 1000.0);
				if (secLeft != lastLoggedSec)
				{
					Log($"[카운트다운] {secLeft}초 ({now:HH:mm:ss.fff})");
					lastLoggedSec = secLeft;
				}

				if (remainMs > 1500)
				{
					// 다음 '초' 경계까지만 자고 다시 로그
					int sleepMs = (int)(remainMs % 1000);
					if (sleepMs < 50) sleepMs += 1000;
					Thread.Sleep(sleepMs);
				}
				else if (remainMs > 50)
				{
					Thread.Sleep((int)remainMs - 50);
				}
				else
				{
					// 마지막 50ms busy-spin
					var sw = Stopwatch.StartNew();
					long waitTicks = (long)(remainMs * Stopwatch.Frequency / 1000.0);
					while (sw.ElapsedTicks < waitTicks) { /* spin */ }
					break;
				}
			}

			Log($"[발사] {ServerNowKst(kstZone):HH:mm:ss.fff} KST");
			Refresh(_curUrl);
		}

		public void ProcessReservation()
		{
			// Step1 현재 시간 체크
			WindowScrollBottom();

			// Step2 대기
			if (data.IsDelay)
			{
				DoDelay();

				// DoDelay에서 이미 정각 새로고침 완료 → 바로 버튼 확인
				if (CheckDate())
					goto Payment;
			}

			while (true)
			{
				if (!HasEnableButton(data.IsDelay) && CheckDate())
				{
					break;
				}

				Refresh(_curUrl);
			}

			Payment:

			// Step3 다음
			//selenium.Click("//*[@id=\"root\"]/main/div[2]/div/button");
			// CheckDate안에서 호출
			Wait();

			// 후킹을 /request/ 도착 직후에 설치해 자동/수동 클릭 모두 캡처되도록 한다.
			// (alert/confirm 도 통과시켜 네이티브 다이얼로그로 멈추는 상황도 로그로 보임)
			InstallSubmitBookingHook();

			// Step4a: templates/submitBooking_{bizItemId}.json 가 있으면 "동의" 버튼 없이 직접 fetch POST.
			// 성공 시 결제 URL 확보 → 메인 창에서 바로 이동. 실패 시 Step4b 로 폴백(기존 버튼 클릭).
			string capturedPayUrl = "";
			string newHandle = "";
			var main_window = driver.CurrentWindowHandle;

			if (TrySubmitBookingDirect(out var directUrl))
			{
				capturedPayUrl = directUrl;
				Log($"★ 결제 URL (direct): {directUrl}");
			}
			else
			{
				// Step4b: "동의" 버튼 — 서버 submitBooking mutation 발생 (slot 10분 홀드), 새 창으로 결제 페이지 오픈.
				// 홀드는 서버 도착 순서로 결정되므로 클릭을 최대한 빨리 쏴야 한다.
				ClickAgreeFast("//*[@id=\"root\"]/div[2]/div[5]/div/button[2]");
			}

			var pollDeadline = DateTime.UtcNow.AddSeconds(20);
			while (DateTime.UtcNow < pollDeadline)
			{
				// (1) fetch 응답 캡처 — 뜨자마자 즉시 로그 (수동 복구용 URL 확보 최우선)
				if (string.IsNullOrEmpty(capturedPayUrl))
				{
					try
					{
						var r = ((IJavaScriptExecutor)driver)
							.ExecuteScript("return window._bookingPayUrl || '';") as string ?? "";
						if (!string.IsNullOrEmpty(r))
						{
							capturedPayUrl = r;
							Log($"★ 결제 URL 캡처: {capturedPayUrl}");
						}
					}
					catch { }
				}

				// (2) submitBooking 에러 체크
				try
				{
					var err = ((IJavaScriptExecutor)driver)
						.ExecuteScript("return window._bookingPayError || '';") as string ?? "";
					if (!string.IsNullOrEmpty(err))
					{
						Log($"submitBooking 에러: {err}");
						break;
					}
				}
				catch { }

				// (3) 새 창 뜨면 핸들 기록
				try
				{
					foreach (var h in driver.WindowHandles)
					{
						if (h != main_window) { newHandle = h; break; }
					}
				}
				catch { }

				// 둘 중 하나라도 확보됐으면 루프 종료
				if (!string.IsNullOrEmpty(newHandle) || !string.IsNullOrEmpty(capturedPayUrl))
					break;

				Thread.Sleep(50);
			}

			// 폴링 종료 후, 성공/실패 상관없이 캡처한 디버그 정보를 로그로 뿌림.
			// (fetch 가 발화했는지, 네이티브 다이얼로그가 떴는지, 요청/응답 바디 등)
			DumpBookingHookDebug();

			if (!string.IsNullOrEmpty(newHandle))
			{
				Log("새 창 전환");
				var changeDrive = driver.SwitchTo().Window(newHandle);
				selenium.ChangeDriver(changeDrive);
				// 새 창 URL 도 로그로 남겨 수동 복구가 가능하게 한다
				try
				{
					var wu = driver.Url ?? "";
					if (!string.IsNullOrEmpty(wu))
						Log($"★ 결제 URL (새 창): {wu}");
				}
				catch { }
			}
			else if (!string.IsNullOrEmpty(capturedPayUrl))
			{
				Log($"메인 창에서 이동: {capturedPayUrl}");
				driver.Navigate().GoToUrl(capturedPayUrl);
			}
			else
			{
				Log("결제 창/URL 타임아웃 — fetch 후킹과 새 창 모두 미발생");
			}

			Wait();

			// Step5 결제하기 — 핵심(slot hold)은 이미 submitBooking 응답으로 확보됨.
			// 여기부터는 보조 작업이므로 실패해도 창을 닫지 않고 사용자가 수동으로 마무리할 수 있게 한다.
			// orders.pay.naver.com 은 /order/bridge/easybooking/{uuid} → /order/checkout/easybooking/{uuid}
			// 로 리디렉션되므로 /checkout/ 가 URL 에 뜰 때까지 짧게 기다린다.
			try
			{
				var ckDeadline = DateTime.UtcNow.AddSeconds(10);
				while (DateTime.UtcNow < ckDeadline)
				{
					try
					{
						var cur = driver.Url ?? "";
						if (cur.Contains("/checkout/") || cur.Contains("orders.pay.naver.com")) break;
					}
					catch { }
					Thread.Sleep(100);
				}

				// 리디렉션 완료 후의 최종 URL 도 로그 (수동 복구용)
				try
				{
					var finalUrl = driver.Url ?? "";
					if (!string.IsNullOrEmpty(finalUrl))
						Log($"★ 현재 결제 페이지: {finalUrl}");
				}
				catch { }

				WindowScrollBottom();
				var payment = "//*[@id=\"root\"]/div/div[2]/div[5]/div/div/div[2]/button";
				selenium.Click(payment);
				Wait();
				Log("결제하기 클릭 완료");
			}
			catch (Exception ex)
			{
				// 결제하기 버튼 XPath 가 안 뜨거나 페이지 구조가 바뀐 경우.
				// slot hold 는 이미 걸려있으니 창을 열어두고 사용자가 수동으로 완료하면 됨.
				Log($"결제하기 자동 클릭 실패 (수동 진행 필요): {ex.Message}");
				try
				{
					var cur = driver.Url ?? "";
					if (!string.IsNullOrEmpty(cur))
						Log($"★ 수동 결제 URL: {cur}");
				}
				catch { }
			}
			
			/* TODO 2차 비번 나중에
			foreach (var v in driver.WindowHandles)
			{
				if (v != main_window && driver.CurrentWindowHandle != v)
				{
					driver.SwitchTo().Window(v); // 팝업 창으로 전환
				}
			}
			Wait();
			selenium.ChangeDriver(driver);

			while (true)
			{
				var style = selenium.GetElem("//*[@id=\"keyboard\"]/table/tbody/tr[1]/td[1]/button/span").GetAttribute("style");
				var base64Urls = Base64ImageExtractor.ExtractBase64Images(style);
				var imageUrl = "";
				foreach (var v in base64Urls)
				{
					imageUrl = v;
					break;
				}

				// Step6 2차 비번
				if (ProcessSPW(imageUrl))
					break;
				else
					Refresh();
			}
			*/
		}

		public void Wait()
		{
			var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10))  // 타임아웃 넉넉히
			{
				PollingInterval = TimeSpan.FromMilliseconds(200),  // 0.2초마다 체크 (기본 0.2초지만 명시)
				Message = "타임아웃: 결제 관련 요소 로드 실패"
			};

			wait.Until(d => (((IJavaScriptExecutor)d).ExecuteScript("return document.readyState") as string ?? "") == "complete");
			Log("페이지 로딩 완료!");
		}

		public bool ProcessSPW(string base64Image)
		{
			var match = Regex.Match(base64Image, @"data:(image|application)/[a-zA-Z0-9.+_-]+;base64,(.*)");
			if (!match.Success) return false;

			string base64Data = match.Groups[2].Value;
			byte[] imageBytes = Convert.FromBase64String(base64Data);
			// GoogleCredential.FromXxx 는 모두 obsolete — CredentialFactory 로 명시적 타입 생성 후 변환이 권장.
			const string credentialsPath = "./norse-wavelet-479101-t1-e59455775bb5.json";
			var serviceAccount = Google.Apis.Auth.OAuth2.CredentialFactory
				.FromFile<Google.Apis.Auth.OAuth2.ServiceAccountCredential>(credentialsPath);
			ImageAnnotatorClient client = new ImageAnnotatorClientBuilder
			{
				GoogleCredential = serviceAccount.ToGoogleCredential()
				//CredentialsPath = "./your_jsoncredentials.json"
				// 간단히 CredentialsPath에 서비스 계정 키(json) 파일을 직접 대입해도 된다.
			}.Build();

			Google.Cloud.Vision.V1.Image image = Google.Cloud.Vision.V1.Image.FromBytes(imageBytes);
			var result = client.DetectText(image);
			var number = result.First().Description;
			var numbers = number.Split('\n');
			if (numbers.Length == 4
				&& numbers[0].Length == 3
				&& numbers[1].Length == 3
				&& numbers[2].Length == 3
				&& numbers[3].Length == 1)
			{
				var btId = new Dictionary<char, string>();
				var x = 1;
				var y = 1;
				foreach (var str in numbers)
				{
					foreach (var c in str)
					{
						btId[c] = $"//*[@id=\"keyboard\"]/table/tbody/tr[{y}]/td[{x}]/button/span";
						x++;
					}

					y++;
					x = 1;
					if (y == 4)
						x = 2;
				}

				Debug.WriteLine(number);
				var password = data.Acc.SPW;
				foreach (var v in password)
				{
					selenium.Click(btId[v]);
					Thread.Sleep(500);
				}
			}
			else
			{
				return false;
			}

			return true;
		}

		public void StartTimer(int seconds = 30)
		{
			timer = new System.Threading.Timer(_ =>
			{
				try
				{
					driver.Navigate().Refresh();
					Log($"새로고침 완료 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
				}
				catch (Exception ex)
				{
					Log($"새로고침 실패: {ex.Message}");
				}
			}, null, 0, seconds * 1000);
		}

		public void EndTimer()
		{
			timer?.Dispose();
		}

		// ChromeDriver 바이너리에서 자동화 감지 시그니처($cdc_) 제거
		private static void PatchChromeDriver(string driverPath)
		{
			try
			{
				var bytes = File.ReadAllBytes(driverPath);
				var pattern = System.Text.Encoding.ASCII.GetBytes("$cdc_");
				bool patched = false;

				for (int i = 0; i <= bytes.Length - pattern.Length; i++)
				{
					bool found = true;
					for (int j = 0; j < pattern.Length; j++)
					{
						if (bytes[i + j] != pattern[j]) { found = false; break; }
					}
					if (found)
					{
						bytes[i] = (byte)'z'; // '$' → 'z' 로 변경 (JS 변수명 깨뜨림)
						patched = true;
					}
				}

				if (patched)
					File.WriteAllBytes(driverPath, bytes);
			}
			catch { }
		}

	}


	public static class Base64ImageExtractor
	{
		// 가장 강력 추천 패턴
		private static readonly string Pattern =
			@"url\([""']?(data:image/(?:png|jpeg|jpg|gif|svg|webp);base64,[A-Za-z0-9+/=]+)";

		/// <summary>
		/// HTML 또는 style 문자열에서 모든 base64 이미지 URL 추출
		/// </summary>
		public static List<string> ExtractBase64Images(string htmlOrStyleText)
		{
			var matches = Regex.Matches(htmlOrStyleText, Pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

			return matches
				.Cast<Match>()
				.Select(m => m.Groups[1].Value)  // url(data:...) 전체
				.Distinct()
				.ToList();
		}

		/// <summary>
		/// base64 데이터 부분만 추출 (data:image/png;base64, 이후만)
		/// </summary>
		public static List<string> ExtractBase64DataOnly(string htmlOrStyleText)
		{
			var matches = Regex.Matches(htmlOrStyleText, Pattern, RegexOptions.IgnoreCase);

			return matches
				.Cast<Match>()
				.Select(m => m.Groups[1].Value)
				.Select(url => Regex.Match(url, @"base64,(.+)", RegexOptions.IgnoreCase).Groups[1].Value)
				.Where(data => !string.IsNullOrEmpty(data))
				.Distinct()
				.ToList();
		}
	}
}

