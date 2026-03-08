using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;

namespace Tennis
{
	public class SeleniumHelper
	{
		private IWebDriver driver;

		public SeleniumHelper(IWebDriver driver)
		{
			this.driver = driver;
		}

		public void ChangeDriver(IWebDriver driver)
		{
			this.driver = driver;
		}

		public void Click(string xpath)
		{
			try
			{
				var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(1));
				wait.Until(ExpectedConditions.ElementToBeClickable(By.XPath(xpath)));
				var button = driver.FindElement(By.XPath(xpath));
				button.Click();
			}
			catch
			{
				Console.WriteLine($"실패: {xpath}");
				Click(xpath);
			}
		}

		public IWebElement GetElem(string xpath)
		{
			try
			{
				return driver.FindElement(By.XPath(xpath));
			}
			catch
			{
				Console.WriteLine($"실패: {xpath}");
				Thread.Sleep(500);
				return GetElem(xpath);
			}
		}

		public void Log(string msg)
		{
			IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
			string script = $"console.log('{msg}');";
			js.ExecuteScript(script);
		}
	}
}
