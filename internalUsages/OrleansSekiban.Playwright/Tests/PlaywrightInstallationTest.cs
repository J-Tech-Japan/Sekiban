using Microsoft.Playwright;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace OrleansSekiban.Playwright.Tests
{
    [TestFixture]
    public class PlaywrightInstallationTest
    {
        [Test]
        public async Task VerifyPlaywrightInstallation()
        {
            Console.WriteLine("Verifying Playwright installation...");
            
            try
            {
                // Create Playwright instance
                var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                
                // Launch browser
                var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true
                });
                
                // Create a new page
                var page = await browser.NewPageAsync();
                
                // Navigate to a simple page
                await page.GotoAsync("about:blank");
                
                // Get the page title
                var title = await page.TitleAsync();
                
                // Close browser
                await browser.CloseAsync();
                
                Console.WriteLine("Playwright installation verified successfully!");
                
                // Simple assertion to verify the test ran
                Assert.That(title, Is.EqualTo(""));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error verifying Playwright installation: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }
    }
}
