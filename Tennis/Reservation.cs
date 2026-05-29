using Google.Cloud.Vision.V1;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Diagnostics;
using System.Text.RegularExpressions;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using WebDriverManager.Helpers;
using OpenQA.Selenium.Interactions;
using Keys = OpenQA.Selenium.Keys;
using ExpectedConditions = SeleniumExtras.WaitHelpers.ExpectedConditions;

namespace Tennis
{
	public class Account
	{
		public string ID = "";
		public string PW = "";
		public string SPW = "";
		public string Profile = "Default";  // Chrome 프로필 폴더명 (chrome://version 에서 확인)
	}

	public class ReservationData
	{
		public Account Acc;
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

	public class ReservationThread
	{
		public ReservationData data;
		private IWebDriver driver;
		private SeleniumHelper selenium;
		private System.Threading.Timer timer;
		private string _curUrl;

		public ReservationThread(ReservationData _data)
		{
			data = _data;
		}

		public void DoStart()
		{
			try
			{
				Thread.Sleep(data.StartDelay);
				Init();
			}
			catch (Exception e)
			{
				return;
			}
		}

		// 실제 Chrome 프로필을 임시 폴더에 복사 → Chrome 실행 중에도 사용 가능, 계정별 독립 실행 가능
		private string SetupTempProfile()
		{
			var srcUserData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
										   "Google", "Chrome", "User Data");
			var srcProfile = Path.Combine(srcUserData, data.Acc.Profile);
			var tempBase = Path.Combine(Path.GetTempPath(), "TennisChromeProfiles", data.Acc.ID);
			var tempProfile = Path.Combine(tempBase, data.Acc.Profile);

			if (Directory.Exists(tempBase))
				Directory.Delete(tempBase, true);
			Directory.CreateDirectory(tempProfile);

			if (Directory.Exists(srcProfile))
			{
				// 로그인 세션 유지에 필요한 파일/폴더만 복사 (캐시 등 대용량 제외)
				foreach (var f in new[] { "Cookies", "Preferences", "Secure Preferences", "Bookmarks" })
				{
					var src = Path.Combine(srcProfile, f);
					if (File.Exists(src))
						try { File.Copy(src, Path.Combine(tempProfile, f)); } catch { }
				}
				foreach (var d in new[] { "Network", "Local Storage", "Session Storage" })
				{
					var src = Path.Combine(srcProfile, d);
					if (Directory.Exists(src))
						try { CopyDirectory(src, Path.Combine(tempProfile, d)); } catch { }
				}
			}

			return tempBase;
		}

		private void CopyDirectory(string source, string dest)
		{
			Directory.CreateDirectory(dest);
			foreach (var f in Directory.GetFiles(source))
				try { File.Copy(f, Path.Combine(dest, Path.GetFileName(f))); } catch { }
			foreach (var d in Directory.GetDirectories(source))
				CopyDirectory(d, Path.Combine(dest, Path.GetFileName(d)));
		}

		private static int _portIncrement = 0;

		private string FindChromePath()
		{
			var paths = new[]
			{
				@"C:\Program Files\Google\Chrome\Application\chrome.exe",
				@"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
							 @"Google\Chrome\Application\chrome.exe")
			};
			return paths.FirstOrDefault(File.Exists)
				   ?? throw new FileNotFoundException("Chrome 실행 파일을 찾을 수 없습니다.");
		}

		public void Init()
		{
			// 프로필 임시 복사 (원본 잠금 회피)
			var tempUserData = SetupTempProfile();

			// 계정마다 다른 디버깅 포트 (동시 실행 지원)
			int port = 9222 + System.Threading.Interlocked.Increment(ref _portIncrement);

			// Chrome을 일반 프로세스로 직접 실행 → ChromeDriver 실행 시 붙는 자동화 플래그가 없음
			Process.Start(new ProcessStartInfo
			{
				FileName = FindChromePath(),
				Arguments = $"--remote-debugging-port={port} " +
							$"--user-data-dir=\"{tempUserData}\" " +
							$"--profile-directory=\"{data.Acc.Profile}\" " +
							"--no-first-run --no-default-browser-check --start-maximized about:blank",
				UseShellExecute = true
			});
			Thread.Sleep(3000); // Chrome 완전 시작 대기

			// ChromeDriver가 이미 실행 중인 Chrome에 붙음 (새 Chrome 실행 X)
			var driverPath = new DriverManager().SetUpDriver(new ChromeConfig(), VersionResolveStrategy.MatchingBrowser);
			var service = ChromeDriverService.CreateDefaultService(Path.GetDirectoryName(driverPath));
			service.HideCommandPromptWindow = true;

			var options = new ChromeOptions();
			options.DebuggerAddress = $"127.0.0.1:{port}";

			driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(15));
			selenium = new SeleniumHelper(driver);

			// 자동화 감지 신호 제거 (CAPTCHA 우회)
			((ChromeDriver)driver).ExecuteCdpCommand("Page.addScriptToEvaluateOnNewDocument",
				new Dictionary<string, object>
				{
					{ "source", @"
						// webdriver 속성 완전 제거
						delete Object.getPrototypeOf(navigator).webdriver;
						Object.defineProperty(navigator, 'webdriver', { get: () => undefined });

						// 플러그인/미디어 디바이스 스푸핑
						Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
						Object.defineProperty(navigator, 'mimeTypes', { get: () => [1, 2, 3] });
						Object.defineProperty(navigator, 'languages', { get: () => ['ko-KR', 'ko', 'en-US', 'en'] });
						Object.defineProperty(navigator, 'platform', { get: () => 'Win32' });
						Object.defineProperty(navigator, 'hardwareConcurrency', { get: () => 8 });
						Object.defineProperty(navigator, 'deviceMemory', { get: () => 8 });
						Object.defineProperty(navigator, 'maxTouchPoints', { get: () => 0 });

						// Chrome 런타임 객체 스푸핑
						window.chrome = {
							app: { isInstalled: false, InstallState: { DISABLED: 'disabled', INSTALLED: 'installed', NOT_INSTALLED: 'not_installed' }, RunningState: { CANNOT_RUN: 'cannot_run', READY_TO_RUN: 'ready_to_run', RUNNING: 'running' } },
							runtime: { OnInstalledReason: {}, OnRestartRequiredReason: {}, PlatformArch: {}, PlatformNaclArch: {}, PlatformOs: {}, RequestUpdateCheckStatus: {} },
							loadTimes: function() {},
							csi: function() {}
						};

						// Permissions API 패치
						const originalQuery = window.navigator.permissions.query;
						window.navigator.permissions.query = (parameters) =>
							parameters.name === 'notifications'
								? Promise.resolve({ state: Notification.permission })
								: originalQuery(parameters);

						// iframe 내 webdriver 제거
						const originalAttachShadow = Element.prototype.attachShadow;
						Element.prototype.attachShadow = function() {
							return originalAttachShadow.apply(this, arguments);
						};
					" }
				});

			Login();
			Thread.Sleep(1000);

			FindCourt();
			Thread.Sleep(1000);

			ProcessReservation();
		}

		// JavaScript + React 호환 방식으로 input 값 설정 (SendKeys/Clipboard 방식보다 안정적)
		private void SetReactInputValue(IWebElement element, string value)
		{
			IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
			js.ExecuteScript(@"
				var el = arguments[0];
				var nativeSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
				nativeSetter.call(el, arguments[1]);
				el.dispatchEvent(new Event('focus', { bubbles: true }));
				el.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true }));
				el.dispatchEvent(new Event('input', { bubbles: true }));
				el.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true }));
				el.dispatchEvent(new Event('change', { bubbles: true }));
			", element, value);
		}

		// CAPTCHA 또는 추가 인증이 발생했을 때 사용자가 수동 해결할 때까지 대기
		private void WaitIfCaptcha()
		{
			Thread.Sleep(2000);
			// 로그인 성공 시 nid.naver.com을 벗어남
			if (driver.Url.Contains("nid.naver.com"))
			{
				Console.WriteLine("[CAPTCHA] 자동입력 방지 또는 추가 인증 감지 - 브라우저에서 직접 해결해 주세요 (최대 3분 대기)");
				var wait = new WebDriverWait(driver, TimeSpan.FromMinutes(3));
				wait.Until(d => !d.Url.Contains("nid.naver.com"));
				Console.WriteLine("[CAPTCHA] 해결됨. 계속 진행합니다.");
			}
		}

		public void Login()
		{
			var random = new Random();
			var actions = new Actions(driver);

			// 네이버 메인 방문 - 쿠키/세션 확인
			driver.Navigate().GoToUrl("https://www.naver.com");
			Wait();
			Thread.Sleep(random.Next(1000, 2000));

			// 이미 로그인된 상태면 (user profile 사용 시) 바로 반환
			if (!driver.Url.Contains("nid.naver.com"))
			{
				try
				{
					// 로그인된 경우 로그아웃 링크가 존재
					driver.FindElement(By.XPath("//*[contains(@href,'logout')]"));
					Console.WriteLine("이미 로그인 상태입니다.");
					return;
				}
				catch { }
			}

			// 로그인 페이지로 이동
			driver.Navigate().GoToUrl("https://nid.naver.com/nidlogin.login");
			Wait();
			Thread.Sleep(random.Next(1000, 2000));

			// 자연스러운 마우스 이동
			try { actions.MoveByOffset(random.Next(100, 400), random.Next(100, 300)).Perform(); } catch { }
			Thread.Sleep(random.Next(300, 700));

			// Id Tab Click
			try
			{
				var loginTab = driver.FindElement(By.XPath("//*[@id=\"loinid\"]/span/span"));
				actions.MoveToElement(loginTab).Click().Perform();
				Thread.Sleep(random.Next(400, 800));
			}
			catch { }

			// 아이디 입력
			var id = selenium.GetElem("//*[@id=\"id\"]");
			actions.MoveToElement(id).Click().Perform();
			Thread.Sleep(random.Next(400, 800));
			SetReactInputValue(id, data.Acc.ID);
			Thread.Sleep(random.Next(600, 1200));

			// 비밀번호 입력
			var pw = selenium.GetElem("//*[@id=\"pw\"]");
			actions.MoveToElement(pw).Click().Perform();
			Thread.Sleep(random.Next(400, 800));
			SetReactInputValue(pw, data.Acc.PW);
			Thread.Sleep(random.Next(600, 1200));

			// 로그인 버튼 클릭
			var enter = selenium.GetElem("//*[@id=\"log.login\"]");
			actions.MoveToElement(enter).Click().Perform();

			// CAPTCHA 감지 - 나타나면 수동으로 해결할 때까지 대기 후 계속 진행
			WaitIfCaptcha();
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
			var findMonth = $"{data.Month}월 {data.CourtNumber}코트";
			var url = "https://booking.naver.com/booking/10/bizes/210031/items";
			driver.Navigate().GoToUrl(url);
			Thread.Sleep(1000);
			var elem = selenium.GetElem("//*[@id=\"root\"]/div[3]/div[2]/div/ul");
			var elems = elem.FindElements(By.ClassName("HomeBookingList__item__ALjH7"));

			foreach (var v  in elems)
			{
				var courtName = v.Text;
				if (courtName.Contains(findMonth))
				{
					if (data.Month == 1 && courtName.Contains("11월"))
						continue;

					v.Click();
					break;
				}
			}

			Thread.Sleep(2000);

			url = driver.Url;
			url = url.Substring(0, url.IndexOf("startDate="));
			var urlAdd = $"endDateTime={data.Year}-{data.Month:D2}-{data.Day:D2}T{data.EndTime:D2}%3A00%3A00%2B09%3A00&startDate={data.Year:D2}-{data.Month:D2}-{data.Day:D2}&startDateTime={data.Year}-{data.Month:D2}-{data.Day:D2}T{data.StartTime:D2}%3A00%3A00%2B09%3A00";
			url += urlAdd;
			_curUrl = url;
			driver.Navigate().GoToUrl(url);
		}

		public void FindNagokCourt()
		{
			var findMonth = $"{data.Month}월 내곡 {data.CourtNumber}코트";
			var url = "https://booking.naver.com/booking/10/bizes/217811/items";
			driver.Navigate().GoToUrl(url);
			Thread.Sleep(1000);
			var elem = selenium.GetElem("//*[@id=\"root\"]/div[3]/div[2]/div[1]/ul");
			var elems = elem.FindElements(By.ClassName("HomeBookingList__item__ALjH7"));

			var idx = 1;
			foreach (var v in elems)
			{
				var courtName = v.Text;
				if (courtName.Contains(findMonth))
				{
					if (data.Month == 1 && courtName.Contains("11월"))
						continue;

					var el = selenium.GetElem($"//*[@id=\"root\"]/div[3]/div[2]/div/ul/li[{idx}]/a/div/div[1]");
					el.Click();
					break;
				}
				idx++;
			}

			Thread.Sleep(2000);

			url = driver.Url;
			url = url.Substring(0, url.IndexOf("startDate="));
			var urlAdd = $"endDateTime={data.Year}-{data.Month:D2}-{data.Day:D2}T{data.EndTime:D2}%3A00%3A00%2B09%3A00&startDate={data.Year:D2}-{data.Month:D2}-{data.Day:D2}&startDateTime={data.Year}-{data.Month:D2}-{data.Day:D2}T{data.StartTime:D2}%3A00%3A00%2B09%3A00";
			url += urlAdd;
			_curUrl = url;
			driver.Navigate().GoToUrl(url);
		}

		public void OnClose()
		{
			driver.Quit();
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
				// 날짜 체크
				WindowScrollBottom();
				var btn = selenium.GetElem("//*[@id=\"root\"]/main/div[2]/div/button");
				if (btn != null)
				{
					if (btn.Text == "다음")
					{
						Console.WriteLine("OK");
						if (btn.Displayed && btn.Enabled)
							btn.Click();
						return true;
					}
				}
			}
			catch
			{
				Wait();
			}
			return false;
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

		public void DoDelay()
		{
			while(true)
			{
				TimeZoneInfo kstZone = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
				DateTime date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, kstZone);
				var remain = (60 - date.Minute - 1) * 60 + (60 - date.Second);
				Console.WriteLine($"남은 시간 : {remain}초");
				WindowScrollBottom();

				var refreshTime = 60;
				if (remain > refreshTime + 7)
				{
					Thread.Sleep(refreshTime * 1000);
					Refresh();
				}
				else
				{
					Thread.Sleep(remain * 1000);
					return;
				}
			}
		}

		public void ProcessReservation()
		{
			// Step1 현재 시간 체크
			WindowScrollBottom();

			// Step2 대기
			if (data.IsDelay)
			{
				DoDelay();
			}

			while (true)
			{
				if (!HasEnableButton(data.IsDelay) && CheckDate())
				{
					break;
				}

				Refresh(_curUrl);
			}

			// Step3 다음
			//selenium.Click("//*[@id=\"root\"]/main/div[2]/div/button");
			// CheckDate안에서 호출
			Wait();

			// Step4 1차 결제 다음
			var main_window = driver.CurrentWindowHandle;
			WindowScrollBottom();
			selenium.Click("//*[@id=\"root\"]/div[2]/div[5]/div/button[2]");
			Wait();

			Thread.Sleep(5000);

			foreach (var v in driver.WindowHandles)
			{
				if (v != main_window)
				{
					var changeDrive = driver.SwitchTo().Window(v);
					selenium.ChangeDriver(changeDrive);
					break;
				}
			}

			Wait();

			// Step5 결제하기
			WindowScrollBottom();
			var payment = "//*[@id=\"root\"]/div/div[2]/div[5]/div/div/div[2]/button";
			selenium.Click(payment);
			Wait();
			
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

			wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString() == "complete");
			Console.WriteLine("페이지 로딩 완료!");
		}

		public bool ProcessSPW(string base64Image)
		{
			var match = Regex.Match(base64Image, @"data:(image|application)/[a-zA-Z0-9.+_-]+;base64,(.*)");
			if (!match.Success) return false;

			string base64Data = match.Groups[2].Value;
			byte[] imageBytes = Convert.FromBase64String(base64Data);
			string credentialsString = File.ReadAllText("./norse-wavelet-479101-t1-e59455775bb5.json");
			ImageAnnotatorClient client = new ImageAnnotatorClientBuilder
			{
				JsonCredentials = credentialsString
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
				var password = "121314";
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
					Console.WriteLine($"새로고침 완료 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"새로고침 실패: {ex.Message}");
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

