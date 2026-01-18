using Microsoft.Playwright;
using System.Diagnostics;
namespace OrleansSekiban.Playwright.Base;

public abstract class BaseTest : IDisposable
{
    protected const string BaseUrl = "https://localhost:7201"; // Default URL, will be overridden if needed
    private readonly HttpClient _httpClient = new();
    private bool _disposed;
    protected Process? AppHostProcess;
    protected IBrowser? Browser;
    protected IBrowserContext? Context;
    protected IPage? Page;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    [SetUp]
    public async Task SetUp()
    {
        Console.WriteLine("=== SETUP PERFORMANCE DEBUGGING ===");
        var totalSetupSw = Stopwatch.StartNew();

        // Start AppHost
        Console.WriteLine("Starting AppHost...");
        var appHostSw = Stopwatch.StartNew();

        try
        {
            // Kill any existing OrleansSekiban processes first
            KillOrleansSekibanProcesses();

            // Get the absolute path to the AppHost directory
            var currentDirectory = Directory.GetCurrentDirectory();
            Console.WriteLine($"Current directory: {currentDirectory}");

            // Try multiple possible paths to find the AppHost directory
            var appHostPath = FindAppHostDirectory(currentDirectory);

            if (string.IsNullOrEmpty(appHostPath))
            {
                throw new DirectoryNotFoundException(
                    "Could not find OrleansSekiban.AppHost directory in any of the expected locations");
            }

            Console.WriteLine($"Found AppHost directory at: {appHostPath}");

            // Start the AppHost
            AppHostProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "run --launch-profile https",
                    WorkingDirectory = appHostPath,
                    UseShellExecute = true,
                    CreateNoWindow = false
                }
            };

            Console.WriteLine("Starting AppHost process...");
            AppHostProcess.Start();

            // Wait for the server to start
            Console.WriteLine("Waiting for AppHost to start...");
            var serverStarted = await WaitForServerToStart();

            if (!serverStarted)
            {
                Console.WriteLine("WARNING: Server did not respond within the timeout period");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting AppHost: {ex.Message}");
        }

        appHostSw.Stop();
        Console.WriteLine($"AppHost started in {appHostSw.ElapsedMilliseconds}ms");

        // Create Playwright instance
        Console.WriteLine("Creating Playwright instance...");
        var playwrightSw = Stopwatch.StartNew();
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        playwrightSw.Stop();
        Console.WriteLine($"Playwright instance created in {playwrightSw.ElapsedMilliseconds}ms");

        // Launch browser with increased timeout and viewport size
        Console.WriteLine("Launching browser...");
        var browserSw = Stopwatch.StartNew();
        Browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions
            {
                Headless = false, // Set to false for debugging
                Timeout = 60000 // 60 seconds
            });
        browserSw.Stop();
        Console.WriteLine($"Browser launched in {browserSw.ElapsedMilliseconds}ms");

        // Create a new browser context with larger viewport
        Console.WriteLine("Creating browser context...");
        var contextSw = Stopwatch.StartNew();
        Context = await Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize
                {
                    Width = 1280,
                    Height = 800
                }
            });
        contextSw.Stop();
        Console.WriteLine($"Browser context created in {contextSw.ElapsedMilliseconds}ms");

        // Create a new page
        Console.WriteLine("Creating new page...");
        var pageSw = Stopwatch.StartNew();
        Page = await Context.NewPageAsync();
        pageSw.Stop();
        Console.WriteLine($"New page created in {pageSw.ElapsedMilliseconds}ms");

        totalSetupSw.Stop();
        Console.WriteLine($"Total setup time: {totalSetupSw.ElapsedMilliseconds}ms");

        // Wait for the postgresql database to be ready
        Console.WriteLine("Waiting for database to be ready...");
        await Task.Delay(5000);
        Console.WriteLine("Waited 5000ms for database to be ready");

    }

    [TearDown]
    public async Task TearDown()
    {
        Console.WriteLine("=== TEARDOWN PERFORMANCE DEBUGGING ===");
        var teardownSw = Stopwatch.StartNew();

        // Close browser
        if (Browser != null)
        {
            Console.WriteLine("Disposing browser...");
            var disposeSw = Stopwatch.StartNew();
            await Browser.DisposeAsync();
            Browser = null;
            disposeSw.Stop();
            Console.WriteLine($"Browser disposed in {disposeSw.ElapsedMilliseconds}ms");
        }

        teardownSw.Stop();
        Console.WriteLine($"Total teardown time: {teardownSw.ElapsedMilliseconds}ms");

        // Kill OrleansSekiban processes
        Console.WriteLine("Killing OrleansSekiban processes...");
        var killSw = Stopwatch.StartNew();

        KillOrleansSekibanProcesses();

        killSw.Stop();
        Console.WriteLine($"OrleansSekiban processes killed in {killSw.ElapsedMilliseconds}ms");
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // Dispose managed resources
            _httpClient.Dispose();

            // Dispose AppHostProcess if it's still running
            if (AppHostProcess != null && !AppHostProcess.HasExited)
            {
                try
                {
                    AppHostProcess.Kill();
                    AppHostProcess.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error disposing AppHostProcess: {ex.Message}");
                }
                AppHostProcess = null;
            }
        }

        _disposed = true;
    }

    private string FindAppHostDirectory(string startingDirectory)
    {
        // List of possible relative paths to try
        var possiblePaths = new[]
        {
            "../../OrleansSekiban.AppHost", // From bin/Debug/net9.0
            "../../../OrleansSekiban.AppHost", // From bin/Debug
            "../../../../OrleansSekiban.AppHost", // From bin
            "../../../../../OrleansSekiban.AppHost", // From Playwright
            "../../..", // To solution root
            "../../../.." // One level up from solution root
        };

        // First try to find the solution root by looking for OrleansSekiban.sln
        var solutionRoot = FindSolutionRoot(startingDirectory);

        if (!string.IsNullOrEmpty(solutionRoot))
        {
            var appHostPath = Path.Combine(solutionRoot, "OrleansSekiban.AppHost");
            Console.WriteLine($"Checking solution-relative path: {appHostPath}");

            if (Directory.Exists(appHostPath))
            {
                return appHostPath;
            }

            var internalUsagePath = Path.Combine(solutionRoot, "pure", "internalUsages", "OrleansSekiban.AppHost");
            Console.WriteLine($"Checking solution-relative internal usage path: {internalUsagePath}");

            if (Directory.Exists(internalUsagePath))
            {
                return internalUsagePath;
            }
        }

        // Try each possible relative path
        foreach (var relativePath in possiblePaths)
        {
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(startingDirectory, relativePath));
                Console.WriteLine($"Checking path: {fullPath}");

                if (Directory.Exists(fullPath))
                {
                    // Check if this is the AppHost directory
                    if (Path.GetFileName(fullPath) == "OrleansSekiban.AppHost")
                    {
                        return fullPath;
                    }

                    // Check if AppHost is a subdirectory
                    var appHostSubdir = Path.Combine(fullPath, "OrleansSekiban.AppHost");
                    if (Directory.Exists(appHostSubdir))
                    {
                        return appHostSubdir;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking path: {ex.Message}");
            }
        }

        // As a last resort, try to find it from the root of the project
        try
        {
            var projectRoot
                = "/Users/tomohisa/dev/GitHub/Sekiban/templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire";
            Console.WriteLine($"Trying hardcoded project root: {projectRoot}");

            if (Directory.Exists(projectRoot))
            {
                var appHostPath = Path.Combine(projectRoot, "OrleansSekiban.AppHost");
                if (Directory.Exists(appHostPath))
                {
                    return appHostPath;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking hardcoded path: {ex.Message}");
        }

        throw new Exception("Error checking hardcoded path");
    }

    private string FindSolutionRoot(string startingDirectory)
    {
        try
        {
            var directory = startingDirectory;

            // Go up the directory tree looking for OrleansSekiban.sln
            while (!string.IsNullOrEmpty(directory))
            {
                Console.WriteLine($"Checking for solution in: {directory}");

                // Check if the solution file exists in this directory
                if (File.Exists(Path.Combine(directory, "OrleansSekiban.sln")) ||
                    File.Exists(Path.Combine(directory, "Sekiban.slnx")))
                {
                    Console.WriteLine($"Found solution root at: {directory}");
                    return directory;
                }

                // Check if we've reached the root directory
                var parent = Directory.GetParent(directory)?.FullName ??
                    throw new Exception("Failed to find solution root");
                if (string.IsNullOrEmpty(parent) || parent == directory)
                {
                    break;
                }

                directory = parent;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error finding solution root: {ex.Message}");
        }

        throw new Exception("Error finding solution root");
    }

    private async Task<bool> WaitForServerToStart()
    {
        const int maxRetries = 30;
        const int retryDelayMs = 1000;

        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                Console.WriteLine($"Checking if server is running (attempt {i + 1}/{maxRetries})...");
                var response = await _httpClient.GetAsync(BaseUrl);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Server is running!");
                    return true;
                }

                Console.WriteLine($"Server returned status code: {response.StatusCode}");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Server not yet available: {ex.Message}");
            }

            await Task.Delay(retryDelayMs);
        }

        return false;
    }

    private void KillOrleansSekibanProcesses()
    {
        try
        {
            // Kill the AppHost process if we started it
            if (AppHostProcess != null && !AppHostProcess.HasExited)
            {
                try
                {
                    AppHostProcess.Kill();
                    Console.WriteLine("AppHost process killed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error killing AppHost process: {ex.Message}");
                }
            }

            // Use pkill to kill all OrleansSekiban processes
            var pkillProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pkill",
                    Arguments = "-9 -f \"OrleansSekiban.\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            pkillProcess.Start();
            var output = pkillProcess.StandardOutput.ReadToEnd();
            var error = pkillProcess.StandardError.ReadToEnd();
            pkillProcess.WaitForExit();

            if (!string.IsNullOrEmpty(output))
            {
                Console.WriteLine($"pkill output: {output}");
            }

            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"pkill error: {error}");
            }

            Console.WriteLine("All OrleansSekiban processes killed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in KillOrleansSekibanProcesses: {ex.Message}");
        }
    }

    protected async Task CloseAnyOpenModals()
    {
        Console.WriteLine("Checking for open modals...");
        var modalSw = Stopwatch.StartNew();

        try
        {
            // Check if there are any open modals
            var modalElements = Page!.Locator(".modal.show");
            var count = await modalElements.CountAsync();

            if (count > 0)
            {
                Console.WriteLine($"Found {count} open modals, attempting to close them");

                // Try to close modals using the close button
                var closeButtons = Page.Locator(".modal.show .btn-close");
                var buttonCount = await closeButtons.CountAsync();

                if (buttonCount > 0)
                {
                    Console.WriteLine($"Found {buttonCount} close buttons");

                    // Click all close buttons
                    for (var i = 0; i < buttonCount; i++)
                    {
                        var closeButton = closeButtons.Nth(i);
                        if (await closeButton.IsVisibleAsync())
                        {
                            await closeButton.ClickAsync(new LocatorClickOptions { Force = true });
                            Console.WriteLine($"Clicked close button {i + 1} at {modalSw.ElapsedMilliseconds}ms");

                            // Wait a bit for the modal to close
                            await Task.Delay(500);
                            Console.WriteLine(
                                $"Waited 500ms after clicking close button at {modalSw.ElapsedMilliseconds}ms");
                        }
                    }
                } else
                {
                    Console.WriteLine("No close buttons found, trying to click outside the modal");

                    // Try clicking outside the modal
                    await Page.Mouse.ClickAsync(10, 10);
                    Console.WriteLine($"Clicked outside modal at {modalSw.ElapsedMilliseconds}ms");

                    await Task.Delay(500);
                    Console.WriteLine($"Waited 500ms after clicking outside at {modalSw.ElapsedMilliseconds}ms");
                }

                // Check if modals are still open
                count = await modalElements.CountAsync();
                if (count > 0)
                {
                    Console.WriteLine($"Still have {count} open modals, trying to press Escape key");

                    // Try pressing Escape key
                    await Page.Keyboard.PressAsync("Escape");
                    Console.WriteLine($"Pressed Escape key at {modalSw.ElapsedMilliseconds}ms");

                    await Task.Delay(500);
                    Console.WriteLine($"Waited 500ms after pressing Escape at {modalSw.ElapsedMilliseconds}ms");

                    // Check again
                    count = await modalElements.CountAsync();
                    if (count > 0)
                    {
                        Console.WriteLine($"Still have {count} open modals, will try to reload the page");
                    } else
                    {
                        Console.WriteLine("Successfully closed all modals");
                    }
                } else
                {
                    Console.WriteLine("Successfully closed all modals");
                }
            } else
            {
                Console.WriteLine("No open modals found");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error closing modals: {ex.Message}");
        }

        modalSw.Stop();
        Console.WriteLine($"Modal check/close completed in {modalSw.ElapsedMilliseconds}ms");
    }

    protected async Task NavigateToPage(string pageName)
    {
        Console.WriteLine($"Navigating to {pageName} page...");
        var navSw = Stopwatch.StartNew();

        // Make sure no modals are open
        await CloseAnyOpenModals();
        Console.WriteLine($"Modal check completed at {navSw.ElapsedMilliseconds}ms");

        // Navigate to the specified page
        var navLink = Page!.Locator("nav a.nav-link", new PageLocatorOptions { HasText = pageName });
        Console.WriteLine($"Found navigation link at {navSw.ElapsedMilliseconds}ms");

        // Use force option to bypass any intercepting elements
        await navLink.ClickAsync(new LocatorClickOptions { Force = true });
        Console.WriteLine($"Clicked navigation link at {navSw.ElapsedMilliseconds}ms");

        // Wait for the page to load
        var networkSw = Stopwatch.StartNew();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        networkSw.Stop();
        Console.WriteLine($"Waited for network idle in {networkSw.ElapsedMilliseconds}ms");

        // Take a screenshot for debugging
        var screenshotSw = Stopwatch.StartNew();
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = $"{pageName.ToLower()}-page.png" });
        screenshotSw.Stop();
        Console.WriteLine($"Screenshot taken in {screenshotSw.ElapsedMilliseconds}ms");

        navSw.Stop();
        Console.WriteLine($"Navigation to {pageName} completed in {navSw.ElapsedMilliseconds}ms");
    }
}
