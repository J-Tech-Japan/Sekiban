using Microsoft.Playwright;
using OrleansSekiban.Playwright.Base;
using OrleansSekiban.Playwright.PageObjects;
using System.Diagnostics;
namespace OrleansSekiban.Playwright.Tests;

[TestFixture]
public class WeatherForecastTests : BaseTest
{

    [SetUp]
    public async Task TestSetUp()
    {
        Console.WriteLine("=== TEST SETUP PERFORMANCE DEBUGGING ===");
        var totalSetupSw = Stopwatch.StartNew();

        // Navigate to the application
        Console.WriteLine("Navigating to application URL...");
        var navigationSw = Stopwatch.StartNew();
        await Page!.GotoAsync(BaseUrl);
        navigationSw.Stop();
        Console.WriteLine($"Initial navigation completed in {navigationSw.ElapsedMilliseconds}ms");

        // Wait for the page to load
        Console.WriteLine("Waiting for network idle...");
        var networkSw = Stopwatch.StartNew();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        networkSw.Stop();
        Console.WriteLine($"Network idle reached in {networkSw.ElapsedMilliseconds}ms");

        // Print the page title for debugging
        var titleSw = Stopwatch.StartNew();
        var pageTitle = await Page.TitleAsync();
        titleSw.Stop();
        Console.WriteLine($"Page title: {pageTitle} (retrieved in {titleSw.ElapsedMilliseconds}ms)");

        // Take a screenshot for debugging
        Console.WriteLine("Taking screenshot...");
        var screenshotSw = Stopwatch.StartNew();
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = "homepage.png" });
        screenshotSw.Stop();
        Console.WriteLine($"Screenshot saved to homepage.png in {screenshotSw.ElapsedMilliseconds}ms");

        // Initialize page objects
        Console.WriteLine("Initializing page objects...");
        var initSw = Stopwatch.StartNew();
        _weatherPage = new WeatherPage(Page);
        initSw.Stop();
        Console.WriteLine($"Page objects initialized in {initSw.ElapsedMilliseconds}ms");

        totalSetupSw.Stop();
        Console.WriteLine($"Total test setup time: {totalSetupSw.ElapsedMilliseconds}ms");
    }
    private WeatherPage _weatherPage = null!;

    [Test]
    public async Task WeatherForecastCrudOperations()
    {
        // Create a stopwatch for timing operations
        var totalStopwatch = Stopwatch.StartNew();

        try
        {
            Console.WriteLine("=== PERFORMANCE DEBUGGING ===");
            Console.WriteLine("Starting weather forecast CRUD test");

            // Navigate to Weather page
            Console.WriteLine("Navigating to Weather page...");
            var navStopwatch = Stopwatch.StartNew();
            await _weatherPage.NavigateToWeatherPage();
            navStopwatch.Stop();
            Console.WriteLine($"Navigation completed in {navStopwatch.ElapsedMilliseconds}ms");

            // Step 1: Verify initially there are no weather forecasts
            Console.WriteLine("Step 1: Verifying initially there are no weather forecasts...");
            var verifyStopwatch = Stopwatch.StartNew();
            var forecastsDisplayed = await _weatherPage.VerifyWeatherForecastsDisplayed();
            verifyStopwatch.Stop();
            Console.WriteLine($"Verification completed in {verifyStopwatch.ElapsedMilliseconds}ms");

            // Assert that no forecasts are displayed initially
            Assert.That(forecastsDisplayed, Is.False, "Weather forecasts should not be displayed initially");

            // Step 2: Add a new weather forecast
            Console.WriteLine("Step 2: Adding a new weather forecast...");
            var addStopwatch = Stopwatch.StartNew();
            await _weatherPage.AddWeatherForecast("Tokyo", "25", "Warm");
            addStopwatch.Stop();
            Console.WriteLine($"Adding completed in {addStopwatch.ElapsedMilliseconds}ms");

            await Task.Delay(500);
            // Verify the forecast was added
            forecastsDisplayed = await _weatherPage.VerifyWeatherForecastsDisplayed();
            Assert.That(forecastsDisplayed, Is.True, "Weather forecast should be displayed after adding");

            // Get the locations to verify the added forecast
            var locations = await _weatherPage.GetWeatherForecastLocations();
            Assert.That(locations.Length, Is.EqualTo(1), "There should be exactly one forecast");
            Assert.That(locations[0], Is.EqualTo("Tokyo"), "The location should be Tokyo");

            // Get temperatures to verify
            var temperatures = await _weatherPage.GetWeatherForecastTemperatures();
            Assert.That(temperatures[0], Is.EqualTo("25"), "The temperature should be 25");

            // Get summaries to verify
            var summaries = await _weatherPage.GetWeatherForecastSummaries();
            Assert.That(summaries[0], Is.EqualTo("Warm"), "The summary should be Warm");

            // Step 3: Add another weather forecast
            Console.WriteLine("Step 3: Adding another weather forecast...");
            addStopwatch.Restart();
            await _weatherPage.AddWeatherForecast("New York", "15", "Cool");
            addStopwatch.Stop();
            Console.WriteLine($"Adding completed in {addStopwatch.ElapsedMilliseconds}ms");

            // Verify there are now two forecasts
            locations = await _weatherPage.GetWeatherForecastLocations();
            Assert.That(locations.Length, Is.EqualTo(2), "There should be exactly two forecasts");

            // Find the indices of Tokyo and New York (don't assume order)
            var tokyoIndex = Array.IndexOf(locations, "Tokyo");
            var newYorkIndex = Array.IndexOf(locations, "New York");

            Assert.That(tokyoIndex, Is.GreaterThanOrEqualTo(0), "Tokyo forecast should exist");
            Assert.That(newYorkIndex, Is.GreaterThanOrEqualTo(0), "New York forecast should exist");

            // Get temperatures and summaries to verify
            temperatures = await _weatherPage.GetWeatherForecastTemperatures();
            summaries = await _weatherPage.GetWeatherForecastSummaries();

            // Verify the Tokyo forecast
            Assert.That(temperatures[tokyoIndex], Is.EqualTo("25"), "Tokyo temperature should be 25");
            Assert.That(summaries[tokyoIndex], Is.EqualTo("Warm"), "Tokyo summary should be Warm");

            // Verify the New York forecast
            Assert.That(temperatures[newYorkIndex], Is.EqualTo("15"), "New York temperature should be 15");
            Assert.That(summaries[newYorkIndex], Is.EqualTo("Cool"), "New York summary should be Cool");

            // Step 4: Edit the Tokyo forecast (note: only location can be edited based on the HTML)
            Console.WriteLine("Step 4: Editing the Tokyo forecast location...");
            var editStopwatch = Stopwatch.StartNew();
            await _weatherPage.EditWeatherForecast(tokyoIndex, "Kyoto", "22", "Hot");
            editStopwatch.Stop();
            Console.WriteLine($"Editing completed in {editStopwatch.ElapsedMilliseconds}ms");

            // Verify the forecast location was edited (temperature and summary remain unchanged)
            locations = await _weatherPage.GetWeatherForecastLocations();

            // Find the new indices after edit
            var kyotoIndex = Array.IndexOf(locations, "Kyoto");
            newYorkIndex = Array.IndexOf(locations, "New York");

            Assert.That(kyotoIndex, Is.GreaterThanOrEqualTo(0), "Kyoto forecast should exist");
            Assert.That(newYorkIndex, Is.GreaterThanOrEqualTo(0), "New York forecast should still exist");
            // Note: We don't assert on temperature and summary since they can't be edited in the form
            Console.WriteLine("Note: Temperature and Summary fields are not editable in the current UI");

            // Step 5: Delete the New York forecast
            Console.WriteLine("Step 5: Deleting the New York forecast...");
            var deleteStopwatch = Stopwatch.StartNew();
            await _weatherPage.DeleteWeatherForecast(newYorkIndex);
            deleteStopwatch.Stop();
            Console.WriteLine($"Deleting completed in {deleteStopwatch.ElapsedMilliseconds}ms");

            // Verify there is now only one forecast
            locations = await _weatherPage.GetWeatherForecastLocations();
            Assert.That(locations.Length, Is.EqualTo(1), "There should be exactly one forecast after deletion");
            Assert.That(locations[0], Is.EqualTo("Kyoto"), "The remaining location should be Kyoto");

            // Step 6: Delete the remaining weather forecast
            Console.WriteLine("Step 6: Deleting the remaining weather forecast...");
            deleteStopwatch.Restart();
            await _weatherPage.DeleteWeatherForecast(0);
            deleteStopwatch.Stop();
            Console.WriteLine($"Deleting completed in {deleteStopwatch.ElapsedMilliseconds}ms");

            // Verify there are no forecasts left
            forecastsDisplayed = await _weatherPage.VerifyWeatherForecastsDisplayed();
            Assert.That(forecastsDisplayed, Is.False, "No weather forecasts should be displayed after deleting all");

            // Take a final screenshot
            await Page!.ScreenshotAsync(new PageScreenshotOptions { Path = "final-state.png" });
            Console.WriteLine("Final screenshot saved to final-state.png");

            // Report total test time
            totalStopwatch.Stop();
            Console.WriteLine("\n=== TEST COMPLETED ===");
            Console.WriteLine($"Total test execution time: {totalStopwatch.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            // Take a screenshot on error
            await Page!.ScreenshotAsync(new PageScreenshotOptions { Path = "error.png" });
            Console.WriteLine("Error screenshot saved to error.png");
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }
}
