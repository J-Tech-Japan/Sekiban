using Microsoft.Playwright;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace OrleansSekiban.Playwright.PageObjects
{
    public class WeatherPage
    {
        private readonly IPage _page;
        private const string PageName = "Weather";

        public WeatherPage(IPage page)
        {
            _page = page;
        }

        public async Task NavigateToWeatherPage()
        {
            Console.WriteLine("  [WeatherPage] Starting navigation to Weather page");
            var sw = Stopwatch.StartNew();
            
            // Navigate to Weather page
            var weatherLink = _page.Locator("nav a.nav-link", new() { HasText = PageName });
            Console.WriteLine($"  [WeatherPage] Found nav link in {sw.ElapsedMilliseconds}ms");
            
            await weatherLink.ClickAsync(new LocatorClickOptions { Force = true });
            Console.WriteLine($"  [WeatherPage] Clicked nav link in {sw.ElapsedMilliseconds}ms");
            
            // Wait for the page to load
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Console.WriteLine($"  [WeatherPage] Waited for network idle in {sw.ElapsedMilliseconds}ms");
            
            sw.Stop();
            Console.WriteLine($"  [WeatherPage] Navigation completed in {sw.ElapsedMilliseconds}ms");
        }

        public async Task<bool> VerifyWeatherForecastsDisplayed()
        {
            Console.WriteLine("  [WeatherPage] Verifying weather forecasts are displayed");
            var sw = Stopwatch.StartNew();
            
            // Wait for the table to be visible
            await _page.WaitForSelectorAsync("table", new PageWaitForSelectorOptions { Timeout = 30000 });
            Console.WriteLine($"  [WeatherPage] Table appeared in {sw.ElapsedMilliseconds}ms");
            
            // Check if there are any rows in the table
            var tableRows = _page.Locator("table tbody tr");
            var rowCount = await tableRows.CountAsync();
            Console.WriteLine($"  [WeatherPage] Found {rowCount} rows in {sw.ElapsedMilliseconds}ms");
            
            // Take a screenshot for debugging
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = "weather-forecasts.png" });
            Console.WriteLine($"  [WeatherPage] Screenshot taken in {sw.ElapsedMilliseconds}ms");
            
            sw.Stop();
            Console.WriteLine($"  [WeatherPage] Verification completed in {sw.ElapsedMilliseconds}ms");
            
            return rowCount > 0;
        }

        public async Task<string[]> GetWeatherForecastLocations()
        {
            Console.WriteLine("  [WeatherPage] Getting weather forecast locations");
            var sw = Stopwatch.StartNew();
            
            // Wait for the table to be visible
            await _page.WaitForSelectorAsync("table", new PageWaitForSelectorOptions { Timeout = 30000 });
            Console.WriteLine($"  [WeatherPage] Table appeared in {sw.ElapsedMilliseconds}ms");
            
            // Get all location cells
            var locationCells = _page.Locator("table tbody tr td:first-child");
            var count = await locationCells.CountAsync();
            Console.WriteLine($"  [WeatherPage] Found {count} location cells in {sw.ElapsedMilliseconds}ms");
            
            // Extract the text from each location cell
            var locations = new string[count];
            for (int i = 0; i < count; i++)
            {
                locations[i] = await locationCells.Nth(i).TextContentAsync() ?? string.Empty;
                Console.WriteLine($"  [WeatherPage] Location {i+1}: {locations[i]}");
            }
            
            sw.Stop();
            Console.WriteLine($"  [WeatherPage] Getting locations completed in {sw.ElapsedMilliseconds}ms");
            
            return locations;
        }

        public async Task<string[]> GetWeatherForecastTemperatures()
        {
            Console.WriteLine("  [WeatherPage] Getting weather forecast temperatures");
            var sw = Stopwatch.StartNew();
            
            // Wait for the table to be visible
            await _page.WaitForSelectorAsync("table", new PageWaitForSelectorOptions { Timeout = 30000 });
            Console.WriteLine($"  [WeatherPage] Table appeared in {sw.ElapsedMilliseconds}ms");
            
            // Get all temperature cells (assuming it's the third column)
            var tempCells = _page.Locator("table tbody tr td:nth-child(3)");
            var count = await tempCells.CountAsync();
            Console.WriteLine($"  [WeatherPage] Found {count} temperature cells in {sw.ElapsedMilliseconds}ms");
            
            // Extract the text from each temperature cell
            var temperatures = new string[count];
            for (int i = 0; i < count; i++)
            {
                temperatures[i] = await tempCells.Nth(i).TextContentAsync() ?? string.Empty;
                Console.WriteLine($"  [WeatherPage] Temperature {i+1}: {temperatures[i]}");
            }
            
            sw.Stop();
            Console.WriteLine($"  [WeatherPage] Getting temperatures completed in {sw.ElapsedMilliseconds}ms");
            
            return temperatures;
        }

        public async Task<string[]> GetWeatherForecastSummaries()
        {
            Console.WriteLine("  [WeatherPage] Getting weather forecast summaries");
            var sw = Stopwatch.StartNew();
            
            // Wait for the table to be visible
            await _page.WaitForSelectorAsync("table", new PageWaitForSelectorOptions { Timeout = 30000 });
            Console.WriteLine($"  [WeatherPage] Table appeared in {sw.ElapsedMilliseconds}ms");
            
            // Get all summary cells (fifth column, not the last which contains actions)
            var summaryCells = _page.Locator("table tbody tr td:nth-child(5)");
            var count = await summaryCells.CountAsync();
            Console.WriteLine($"  [WeatherPage] Found {count} summary cells in {sw.ElapsedMilliseconds}ms");
            
            // Extract the text from each summary cell
            var summaries = new string[count];
            for (int i = 0; i < count; i++)
            {
                summaries[i] = await summaryCells.Nth(i).TextContentAsync() ?? string.Empty;
                Console.WriteLine($"  [WeatherPage] Summary {i+1}: {summaries[i]}");
            }
            
            sw.Stop();
            Console.WriteLine($"  [WeatherPage] Getting summaries completed in {sw.ElapsedMilliseconds}ms");
            
            return summaries;
        }

        public async Task AddWeatherForecast(string location, string temperature, string summary)
        {
            Console.WriteLine("  [WeatherPage] Adding new weather forecast");
            var sw = Stopwatch.StartNew();
            
            // Click the Add button - using more specific locator to avoid ambiguity
            var addButton = _page.GetByRole(AriaRole.Button, new() { Name = "Add New Weather Forecast" });
            await addButton.ClickAsync();
            Console.WriteLine($"  [WeatherPage] Clicked Add button in {sw.ElapsedMilliseconds}ms");
            
            // Wait for the modal to appear
            await _page.WaitForSelectorAsync("#addForecastModal", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
            Console.WriteLine($"  [WeatherPage] Modal appeared in {sw.ElapsedMilliseconds}ms");
            
            // Take a screenshot of the form for debugging
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = "form-before-fill.png" });
            Console.WriteLine($"  [WeatherPage] Form screenshot taken in {sw.ElapsedMilliseconds}ms");
            
            // Debug: Print all input fields on the page
            var inputs = _page.Locator("input");
            var inputCount = await inputs.CountAsync();
            Console.WriteLine($"  [WeatherPage] Found {inputCount} input fields on the page");
            
            for (int i = 0; i < inputCount; i++)
            {
                var input = inputs.Nth(i);
                var name = await input.GetAttributeAsync("name") ?? "no-name";
                var type = await input.GetAttributeAsync("type") ?? "no-type";
                var id = await input.GetAttributeAsync("id") ?? "no-id";
                Console.WriteLine($"  [WeatherPage] Input {i}: name='{name}', type='{type}', id='{id}'");
            }
            
            // Fill in the form fields using the exact IDs from the HTML
            try
            {
                // Fill location field
                await _page.Locator("#location").FillAsync(location);
                Console.WriteLine($"  [WeatherPage] Filled location field in {sw.ElapsedMilliseconds}ms");
                
                // Set today's date for the date field
                var today = DateTime.Today.ToString("yyyy-MM-dd");
                await _page.Locator("#date").FillAsync(today);
                Console.WriteLine($"  [WeatherPage] Filled date field with {today} in {sw.ElapsedMilliseconds}ms");
                
                // Fill temperature field
                await _page.Locator("#temperatureC").FillAsync(temperature);
                Console.WriteLine($"  [WeatherPage] Filled temperature field in {sw.ElapsedMilliseconds}ms");
                
                // Select summary from dropdown
                var summarySelect = _page.Locator("#summary");
                await summarySelect.SelectOptionAsync(new[] { new SelectOptionValue { Label = summary } });
                Console.WriteLine($"  [WeatherPage] Selected summary '{summary}' in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [WeatherPage] Error filling form with ID selectors: {ex.Message}");
                
                try
                {
                    // Try by name attribute
                    await _page.Locator("input[name='forecastModel.Location']").FillAsync(location);
                    
                    var today = DateTime.Today.ToString("yyyy-MM-dd");
                    await _page.Locator("input[name='forecastModel.Date']").FillAsync(today);
                    
                    await _page.Locator("input[name='forecastModel.TemperatureC']").FillAsync(temperature);
                    
                    await _page.Locator("select[name='forecastModel.Summary']")
                        .SelectOptionAsync(new[] { new SelectOptionValue { Label = summary } });
                    
                    Console.WriteLine($"  [WeatherPage] Filled form using name selectors in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"  [WeatherPage] Error filling form with name selectors: {ex2.Message}");
                    
                    try
                    {
                        // Try by label
                        await _page.GetByLabel("Location").FillAsync(location);
                        
                        var today = DateTime.Today.ToString("yyyy-MM-dd");
                        await _page.GetByLabel("Date").FillAsync(today);
                        
                        await _page.GetByLabel("Temperature (°C)").FillAsync(temperature);
                        
                        // For select, try to find it by label and then select the option
                        var summaryDropdown = _page.GetByLabel("Summary");
                        await summaryDropdown.SelectOptionAsync(new[] { new SelectOptionValue { Label = summary } });
                        
                        Console.WriteLine($"  [WeatherPage] Filled form using label selectors in {sw.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex3)
                    {
                        Console.WriteLine($"  [WeatherPage] Error filling form with label selectors: {ex3.Message}");
                        Console.WriteLine("  [WeatherPage] All attempts to fill the form failed");
                    }
                }
            }
            
            // Take a screenshot after filling the form
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = "form-after-fill.png" });
            
            // Submit the form by clicking the Add Forecast button
            try
            {
                var addForecastButton = _page.Locator("#addForecastModal button[type='submit']");
                await addForecastButton.ClickAsync();
                Console.WriteLine($"  [WeatherPage] Clicked Add Forecast button by selector in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [WeatherPage] Error clicking Add Forecast button by selector: {ex.Message}");
                
                try
                {
                    var addForecastButton = _page.GetByRole(AriaRole.Button, new() { Name = "Add Forecast" });
                    await addForecastButton.ClickAsync();
                    Console.WriteLine($"  [WeatherPage] Clicked Add Forecast button by role in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"  [WeatherPage] Error clicking Add Forecast button by role: {ex2.Message}");
                    
                    // Try by text as a last resort
                    var addForecastButton = _page.GetByText("Add Forecast", new() { Exact = true });
                    await addForecastButton.ClickAsync();
                    Console.WriteLine($"  [WeatherPage] Clicked Add Forecast button by text in {sw.ElapsedMilliseconds}ms");
                }
            }
            
            // Wait for the modal to be closed
            try
            {
                await _page.WaitForSelectorAsync("#addForecastModal", new PageWaitForSelectorOptions 
                { 
                    State = WaitForSelectorState.Hidden, 
                    Timeout = 5000 
                });
                Console.WriteLine($"  [WeatherPage] Modal closed automatically in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [WeatherPage] Modal did not close automatically: {ex.Message}");
                
                // Try to close the modal manually
                try
                {
                    // Try clicking the close button
                    var closeButton = _page.Locator("#addForecastModal .btn-close");
                    await closeButton.ClickAsync();
                    Console.WriteLine($"  [WeatherPage] Clicked close button in {sw.ElapsedMilliseconds}ms");
                    
                    // Wait for the modal to be closed
                    await _page.WaitForSelectorAsync("#addForecastModal", new PageWaitForSelectorOptions 
                    { 
                        State = WaitForSelectorState.Hidden, 
                        Timeout = 5000 
                    });
                    Console.WriteLine($"  [WeatherPage] Modal closed after clicking close button in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"  [WeatherPage] Error closing modal with close button: {ex2.Message}");
                    
                    // Try using JavaScript to close the modal
                    await _page.EvaluateAsync("$('#addForecastModal').modal('hide')");
                    Console.WriteLine($"  [WeatherPage] Closed modal with JavaScript in {sw.ElapsedMilliseconds}ms");
                }
            }
            
            // Wait for the table to update
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Console.WriteLine($"  [WeatherPage] Waited for network idle in {sw.ElapsedMilliseconds}ms");
            
            // Take a screenshot for debugging
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = "after-add.png" });
            
            sw.Stop();
            Console.WriteLine($"  [WeatherPage] Adding forecast completed in {sw.ElapsedMilliseconds}ms");
        }

        public async Task EditWeatherForecast(int rowIndex, string newLocation, string newTemperature, string newSummary)
        {
            Console.WriteLine($"  [WeatherPage] Editing weather forecast at row {rowIndex}");
            var sw = Stopwatch.StartNew();
            
            // Click the Edit button for the specified row
            var editButtons = _page.Locator("table tbody tr button:has-text('Edit')");
            await editButtons.Nth(rowIndex).ClickAsync();
            Console.WriteLine($"  [WeatherPage] Clicked Edit button in {sw.ElapsedMilliseconds}ms");
            
            // Wait for the edit modal to appear
            await _page.WaitForSelectorAsync("#editLocationModal", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
            Console.WriteLine($"  [WeatherPage] Edit modal appeared in {sw.ElapsedMilliseconds}ms");
            
            // Take a screenshot of the form for debugging
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = "edit-form-before-fill.png" });
            Console.WriteLine($"  [WeatherPage] Form screenshot taken in {sw.ElapsedMilliseconds}ms");
            
            // Debug: Print all input fields on the page
            var inputs = _page.Locator("input");
            var inputCount = await inputs.CountAsync();
            Console.WriteLine($"  [WeatherPage] Found {inputCount} input fields on the page");
            
            for (int i = 0; i < inputCount; i++)
            {
                var input = inputs.Nth(i);
                var name = await input.GetAttributeAsync("name") ?? "no-name";
                var type = await input.GetAttributeAsync("type") ?? "no-type";
                var id = await input.GetAttributeAsync("id") ?? "no-id";
                Console.WriteLine($"  [WeatherPage] Input {i}: name='{name}', type='{type}', id='{id}'");
            }
            
            // Based on the HTML, the edit form only has a location field
            try
            {
                // Try by ID
                var locationInput = _page.Locator("#newLocation");
                await locationInput.ClearAsync();
                await locationInput.FillAsync(newLocation);
                Console.WriteLine($"  [WeatherPage] Filled location field in {sw.ElapsedMilliseconds}ms");
                
                // Note: The edit form doesn't have temperature or summary fields based on the HTML
                Console.WriteLine($"  [WeatherPage] Note: Temperature and Summary fields not available in edit form");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [WeatherPage] Error filling form with ID selectors: {ex.Message}");
                
                try
                {
                    // Try by name attribute
                    var locationInput = _page.Locator("input[name='editLocationModel.NewLocation']");
                    await locationInput.ClearAsync();
                    await locationInput.FillAsync(newLocation);
                    Console.WriteLine($"  [WeatherPage] Filled form using name selectors in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"  [WeatherPage] Error filling form with name selectors: {ex2.Message}");
                    
                    try
                    {
                        // Try by label
                        var locationInput = _page.GetByLabel("New Location");
                        await locationInput.ClearAsync();
                        await locationInput.FillAsync(newLocation);
                        Console.WriteLine($"  [WeatherPage] Filled form using label selectors in {sw.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex3)
                    {
                        Console.WriteLine($"  [WeatherPage] Error filling form with label selectors: {ex3.Message}");
                        
                        // Try by index as a last resort
                        var formInputs = _page.Locator("#editLocationModal form input");
                        await formInputs.Nth(0).ClearAsync();
                        await formInputs.Nth(0).FillAsync(newLocation);
                        Console.WriteLine($"  [WeatherPage] Filled form using index selectors in {sw.ElapsedMilliseconds}ms");
                    }
                }
            }
            
            // Take a screenshot after filling the form
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = "edit-form-after-fill.png" });
            
            // Submit the form by clicking the Update Location button
            try
            {
                var updateButton = _page.Locator("#editLocationModal button[type='submit']");
                await updateButton.ClickAsync();
                Console.WriteLine($"  [WeatherPage] Clicked Update Location button by selector in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [WeatherPage] Error clicking Update Location button by selector: {ex.Message}");
                
                try
                {
                    var updateButton = _page.GetByRole(AriaRole.Button, new() { Name = "Update Location" });
                    await updateButton.ClickAsync();
                    Console.WriteLine($"  [WeatherPage] Clicked Update Location button by role in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"  [WeatherPage] Error clicking Update Location button by role: {ex2.Message}");
                    
                    // Try by text as a last resort
                    var updateButton = _page.GetByText("Update Location", new() { Exact = true });
                    await updateButton.ClickAsync();
                    Console.WriteLine($"  [WeatherPage] Clicked Update Location button by text in {sw.ElapsedMilliseconds}ms");
                }
            }
            
            // Wait for the modal to be closed
            try
            {
                await _page.WaitForSelectorAsync("#editLocationModal", new PageWaitForSelectorOptions 
                { 
                    State = WaitForSelectorState.Hidden, 
                    Timeout = 5000 
                });
                Console.WriteLine($"  [WeatherPage] Edit modal closed automatically in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [WeatherPage] Edit modal did not close automatically: {ex.Message}");
                
                // Try to close the modal manually
                try
                {
                    // Try clicking the close button
                    var closeButton = _page.Locator("#editLocationModal .btn-close");
                    await closeButton.ClickAsync();
                    Console.WriteLine($"  [WeatherPage] Clicked edit modal close button in {sw.ElapsedMilliseconds}ms");
                    
                    // Wait for the modal to be closed
                    await _page.WaitForSelectorAsync("#editLocationModal", new PageWaitForSelectorOptions 
                    { 
                        State = WaitForSelectorState.Hidden, 
                        Timeout = 5000 
                    });
                    Console.WriteLine($"  [WeatherPage] Edit modal closed after clicking close button in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"  [WeatherPage] Error closing edit modal with close button: {ex2.Message}");
                    
                    // Try using JavaScript to close the modal
                    await _page.EvaluateAsync("$('#editLocationModal').modal('hide')");
                    Console.WriteLine($"  [WeatherPage] Closed edit modal with JavaScript in {sw.ElapsedMilliseconds}ms");
                }
            }
            
            // Wait for the table to update
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Console.WriteLine($"  [WeatherPage] Waited for network idle in {sw.ElapsedMilliseconds}ms");
            
            // Take a screenshot for debugging
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = "after-edit.png" });
            
            sw.Stop();
            Console.WriteLine($"  [WeatherPage] Editing forecast completed in {sw.ElapsedMilliseconds}ms");
        }

        public async Task DeleteWeatherForecast(int rowIndex)
        {
            Console.WriteLine($"  [WeatherPage] Deleting weather forecast at row {rowIndex}");
            var sw = Stopwatch.StartNew();
            
            // Take a screenshot before deleting
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = "before-delete.png" });
            
            // Try different selectors for the Delete button
            try
            {
                // Try by text (using "Remove" as seen in the error message)
                var deleteButtons = _page.Locator("table tbody tr button:has-text('Remove')");
                await deleteButtons.Nth(rowIndex).ClickAsync();
                Console.WriteLine($"  [WeatherPage] Clicked Delete button by text in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [WeatherPage] Error clicking delete button by text: {ex.Message}");
                
                try
                {
                    // Try by role
                    var rows = _page.Locator("table tbody tr");
                    var row = rows.Nth(rowIndex);
                    var deleteButton = row.GetByRole(AriaRole.Button, new() { Name = "Remove" });
                    await deleteButton.ClickAsync();
                    Console.WriteLine($"  [WeatherPage] Clicked Delete button by role in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"  [WeatherPage] Error clicking delete button by role: {ex2.Message}");
                    
                    // Try by class as a last resort
                    var rows = _page.Locator("table tbody tr");
                    var row = rows.Nth(rowIndex);
                    var deleteButton = row.Locator("button.btn-danger");
                    await deleteButton.ClickAsync();
                    Console.WriteLine($"  [WeatherPage] Clicked Delete button by class in {sw.ElapsedMilliseconds}ms");
                }
            }
            
            // Wait for confirmation dialog if there is one
            await _page.WaitForTimeoutAsync(1000); // Wait a bit for any dialog to appear
            
            // Take a screenshot after clicking delete
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = "after-delete-click.png" });
            
            // Try different selectors for the Confirm button
            try
            {
                var confirmButton = _page.GetByText("Confirm");
                if (await confirmButton.CountAsync() > 0)
                {
                    await confirmButton.ClickAsync();
                    Console.WriteLine($"  [WeatherPage] Clicked Confirm button by text in {sw.ElapsedMilliseconds}ms");
                }
                else
                {
                    Console.WriteLine($"  [WeatherPage] No confirmation dialog found");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [WeatherPage] Error with confirmation dialog: {ex.Message}");
                
                try
                {
                    var confirmButton = _page.GetByRole(AriaRole.Button, new() { Name = "Confirm" });
                    if (await confirmButton.CountAsync() > 0)
                    {
                        await confirmButton.ClickAsync();
                        Console.WriteLine($"  [WeatherPage] Clicked Confirm button by role in {sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"  [WeatherPage] Error with confirmation dialog by role: {ex2.Message}");
                    
                    // Try by class as a last resort
                    var confirmButton = _page.Locator("button.btn-primary:has-text('Yes')");
                    if (await confirmButton.CountAsync() > 0)
                    {
                        await confirmButton.ClickAsync();
                        Console.WriteLine($"  [WeatherPage] Clicked Yes button by class in {sw.ElapsedMilliseconds}ms");
                    }
                }
            }
            
            // Wait for the table to update
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Console.WriteLine($"  [WeatherPage] Waited for network idle in {sw.ElapsedMilliseconds}ms");
            
            // Take a screenshot for debugging
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = "after-delete.png" });
            
            sw.Stop();
            Console.WriteLine($"  [WeatherPage] Deleting forecast completed in {sw.ElapsedMilliseconds}ms");
        }
    }
}
