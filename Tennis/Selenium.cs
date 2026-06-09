using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;

namespace Tennis
{
	public class SeleniumHelper
	{
		private IWebDriver driver;
		public Action<string>? LogAction;

		public SeleniumHelper(IWebDriver driver)
		{
			this.driver = driver;
		}

		public void ChangeDriver(IWebDriver driver)
		{
			this.driver = driver;
		}

		public void Click(string xpath, int maxRetries = 3)
		{
			for (int i = 0; i < maxRetries; i++)
			{
				try
				{
					var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(1));
					wait.Until(ExpectedConditions.ElementToBeClickable(By.XPath(xpath)));
					driver.FindElement(By.XPath(xpath)).Click();
					return;
				}
				catch
				{
					if (i < maxRetries - 1)
					{
						Thread.Sleep(500);
						continue;
					}
					WriteLog($"클릭 실패 ({maxRetries}회): {xpath}");
					throw;
				}
			}
		}

		public IWebElement GetElem(string xpath, int maxRetries = 10)
		{
			Exception? lastEx = null;
			for (int i = 0; i < maxRetries; i++)
			{
				try
				{
					return driver.FindElement(By.XPath(xpath));
				}
				catch (Exception ex)
				{
					lastEx = ex;
					if (i < maxRetries - 1)
						Thread.Sleep(500);
				}
			}
			WriteLog($"요소 찾기 실패 ({maxRetries}회): {xpath}");
			throw lastEx!;
		}

		private void WriteLog(string msg)
		{
			LogAction?.Invoke(msg);
		}

		public void Log(string msg)
		{
			WriteLog(msg);
			try
			{
				IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
				js.ExecuteScript($"console.log('{msg}');");
			}
			catch { }
		}
	}
}
