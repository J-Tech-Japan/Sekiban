using System.Diagnostics;

namespace CreateSekibanDcbTemplate;

/// <summary>
/// Installs Sekiban DCB templates and generates an Orleans + API + Blazor project.
/// </summary>
internal class Program
{
    /// <summary>
    /// Main entry point for the tool.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Exit code (0 for success, non-zero for failure)</returns>
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            ShowHelp();
            return 0;
        }

        Console.WriteLine("=== Sekiban Template Generator ===");
        Console.WriteLine();

        string? projectName = null;
        string? templateType = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-t" || args[i] == "--type")
            {
                if (i + 1 < args.Length)
                {
                    templateType = args[i + 1];
                    i++;
                }
            }
            else if (!args[i].StartsWith("-"))
            {
                projectName = args[i];
            }
        }

        if (string.IsNullOrWhiteSpace(projectName))
        {
            Console.Write("Enter project name (default: Contoso.Dcb): ");
            var input = Console.ReadLine();
            projectName = string.IsNullOrWhiteSpace(input) ? "Contoso.Dcb" : input.Trim();
        }

        if (string.IsNullOrWhiteSpace(templateType))
        {
            Console.Write("Select template type (dcb/pure) [default: dcb]: ");
            var input = Console.ReadLine();
            templateType = string.IsNullOrWhiteSpace(input) ? "dcb" : input.Trim();
        }

        templateType = templateType.ToLowerInvariant();
        if (templateType != "dcb" && templateType != "pure")
        {
             Console.Error.WriteLine($"❌ Invalid template type: {templateType}. Allowed values are 'dcb' or 'pure'.");
             return 1;
        }

        Console.WriteLine($"Target Project Name: {projectName}");
        Console.WriteLine($"Template Type: {templateType}");
        Console.WriteLine();

        string installCommand;
        string createCommand;

        if (templateType == "pure")
        {
             installCommand = "new install Sekiban.Pure.Templates";
             createCommand = $"new sekiban-orleans-aspire -n {projectName}";
        }
        else
        {
             installCommand = "new install Sekiban.Dcb.Templates";
             createCommand = $"new sekiban-dcb-orleans -n {projectName}";
        }

        var installExitCode = await RunDotnetCommandAsync(installCommand);

        if (installExitCode != 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"❌ dotnet {installCommand} failed with exit code {installExitCode}.");
            return installExitCode;
        }

        Console.WriteLine();
        Console.WriteLine("✅ Template installation completed successfully.");
        Console.WriteLine();

        var createExitCode = await RunDotnetCommandAsync(createCommand);

        if (createExitCode != 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"❌ dotnet {createCommand} failed with exit code {createExitCode}.");
            return createExitCode;
        }

        Console.WriteLine();
        Console.WriteLine("✅ Project generation completed successfully.");
        Console.WriteLine($"📁 Project created: {projectName}");
        Console.WriteLine();

        return 0;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Usage: create-sekiban-dcb-template [ProjectName] [Options]");
        Console.WriteLine("       dnx CreateSekibanDcbTemplate [ProjectName] [Options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  ProjectName  The name of the project to create. If omitted, you will be prompted.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -t, --type   The type of template to use (dcb or pure). Default is dcb.");
        Console.WriteLine("  -h, --help   Show this help message.");
    }

    /// <summary>
    /// Runs a dotnet command as an external process.
    /// </summary>
    /// <param name="arguments">Command arguments for dotnet</param>
    /// <returns>Exit code of the process</returns>
    private static async Task<int> RunDotnetCommandAsync(string arguments)
    {
        Console.WriteLine($"▶️  Executing: dotnet {arguments}");
        Console.WriteLine();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            Console.Error.WriteLine("Failed to start dotnet process.");
            return -1;
        }

        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
