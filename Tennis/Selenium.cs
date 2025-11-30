using OpenQA.Selenium;

namespace Tennis
{
	public class SeleniumHelper
	{
		private IWebDriver driver;

		public SeleniumHelper(IWebDriver driver)
		{
			this.driver = driver;
		}

		public void Click(string xpath)
		{
			try
			{
				var button = driver.FindElement(By.XPath(xpath));
				button.Click();
			}
			catch
			{
				Console.WriteLine($"실패: {xpath}");
				Thread.Sleep(500);
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
