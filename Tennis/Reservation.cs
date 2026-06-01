using Google.Cloud.Vision.V1;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Diagnostics;
using System.Text.RegularExpressions;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using WebDriverManager.Helpers;

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

		// 멀티스레드 동시 실행 시 ChromeDriver 셋업 충돌 방지
		private static readonly object _driverSetupLock = new object();

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

		private void CopyDirectory(string source, string dest)
		{
			Directory.CreateDirectory(dest);
			foreach (var f in Directory.GetFiles(source))
				try { File.Copy(f, Path.Combine(dest, Path.GetFileName(f))); } catch { }
			foreach (var d in Directory.GetDirectories(source))
				CopyDirectory(d, Path.Combine(dest, Path.GetFileName(d)));
		}

		public void Init()
		{
			// Option
			var options = new ChromeOptions();
			var agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
			//string userProfile = @"C:\Users\honggmbo\AppData\Local\Google\Chrome\User Data";
			//options.AddArgument($"--user-data-dir={userProfile}");
			//options.AddArgument("--profile-directory=Default");   // 또는 "Profile 1" 등
			options.AddArgument($"--user-agent={agent}");
			options.AddArgument("--disable-blink-features=AutomationControlled");
			options.AddArgument("disable-gpu");
			options.AddArgument("--start-maximized");
			options.AddExcludedArgument("enable-automation");

			// SetUpDriver는 드라이버 바이너리를 다운로드/교체하므로
			// 여러 스레드가 동시에 호출하면 파일 잠금 충돌 발생 → lock으로 직렬화
			string driverPath;
			lock (_driverSetupLock)
			{
				driverPath = new DriverManager().SetUpDriver(new ChromeConfig(), VersionResolveStrategy.MatchingBrowser);
			}

			var service = ChromeDriverService.CreateDefaultService(Path.GetDirectoryName(driverPath));
			service.HideCommandPromptWindow = true;

			// ChromeDriver 자동 다운로드 및 설정
			driver = new ChromeDriver(service, options);
			selenium = new SeleniumHelper(driver);

			Login();
			Thread.Sleep(1000);

			FindCourt();
			Thread.Sleep(1000);

			ProcessReservation();
		}

		// CAPTCHA 또는 추가 인증이 발생했을 때 사용자가 수동 해결할 때까지 대기
		private void WaitIfCaptcha(string url, string nextUrl)
		{
			Thread.Sleep(2000);
			// 로그인 성공 시 nid.naver.com을 벗어남
			if (driver.Url.Contains(url))
			{
				Console.WriteLine("[CAPTCHA] 자동입력 방지 또는 추가 인증 감지 - 브라우저에서 직접 해결해 주세요 (최대 3분 대기)");
				var wait = new WebDriverWait(driver, TimeSpan.FromMinutes(10));
				string uuu = driver.Url;
				wait.Until(d => d.Url.Contains(nextUrl));
				Console.WriteLine("[CAPTCHA] 해결됨. 계속 진행합니다.");
			}
		}

		public void Login()
		{
			// 로그인 페이지로 이동
			driver.Navigate().GoToUrl("https://nid.naver.com/nidlogin.login");
			Wait();
			WaitIfCaptcha("nid.naver.com", "nid.naver.com");

			// Id Tab Click
			selenium.Click("//*[@id=\"log.otnlogtab\"]/span/span");

			// CAPTCHA 감지 - 나타나면 수동으로 해결할 때까지 대기 후 계속 진행
			WaitIfCaptcha("nid.naver.com", "https://www.naver.com/");
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
			var elems = elem.FindElements(By.ClassName("HomeBookingList__desc_title__cDcd7"));

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
			var elem = selenium.GetElem("//*[@id=\"root\"]/div[3]/div[2]/div/ul");
			var elems = elem.FindElements(By.ClassName("HomeBookingList__desc_title__cDcd7"));

			var idx = 1;
			foreach (var v in elems)
			{
				var courtName = v.Text;
				if (courtName.Contains(findMonth))
				{
					if (data.Month == 1 && courtName.Contains("11월"))
						continue;

					v.Click();
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

		// 예약 오픈 시각까지 대기. 정각 N초 전부터는 새로고침 없이 정밀 대기 후 리턴.
		private const int RefreshIntervalSec = 60;   // 오픈까지 여유 있을 때 새로고침 주기

		public void DoDelay()
		{
			TimeZoneInfo kstZone = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");

			// targetHour를 최초 진입 시 한 번만 계산 - Phase 1/2에서 재계산하면
			// Refresh 지연 등으로 정각을 살짝 지났을 때 다음 정각으로 넘어가는 버그 발생
			DateTime now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, kstZone);
			DateTime targetHour = now.AddHours(1).Date.AddHours(now.AddHours(1).Hour);

			// Phase 1: 정각 1분 전까지 1분마다 새로고침하며 대기
			while (true)
			{
				now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, kstZone);
				TimeSpan remain = targetHour - now;

				if (remain.TotalSeconds <= 0)
				{
					// 이미 정각을 지남 - 바로 새로고침하고 리턴
					Refresh(_curUrl);
					return;
				}

				Console.WriteLine($"[대기] 정각까지 {remain.Minutes:D2}분 {remain.Seconds:D2}초 남음 ({now:HH:mm:ss} KST)");

				if (remain.TotalSeconds <= RefreshIntervalSec)
					break;  // 1분 이내 → Phase 2로

				WindowScrollBottom();
				Thread.Sleep(RefreshIntervalSec * 1000);  // 1분 대기
				Refresh(_curUrl);                // 1분마다 새로고침
			}

			// Phase 2: 정각까지 정밀 대기 후 Refresh
			{
				now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, kstZone);
				TimeSpan remain = targetHour - now;

				if (remain.TotalMilliseconds > 0)
					Thread.Sleep((int)remain.TotalMilliseconds);

				Refresh(_curUrl);
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

