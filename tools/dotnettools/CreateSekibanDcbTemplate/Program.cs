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
            Console.WriteLine("Select template type:");
            Console.WriteLine("  1. decider        - Sekiban Dcb Decider Aspire Template (recommended)");
            Console.WriteLine("  2. dcb            - Sekiban Dcb Orleans Aspire Template");
            Console.WriteLine("  3. withoutresult  - Sekiban Dcb Orleans Aspire Template (WithoutResult)");
            Console.WriteLine("  4. pure           - Sekiban Pure Orleans Aspire Template");
            Console.WriteLine("  5. decider-aws    - Sekiban Dcb Decider AWS Template (DynamoDB)");
            Console.WriteLine("  6. withoutresult-aws - Sekiban Dcb Orleans AWS Template (DynamoDB)");
            Console.Write("Enter choice [default: decider]: ");
            var input = Console.ReadLine();
            templateType = string.IsNullOrWhiteSpace(input) ? "decider" : input.Trim();
        }

        templateType = templateType.ToLowerInvariant();
        // Allow numeric shortcuts
        templateType = templateType switch
        {
            "1" => "decider",
            "2" => "dcb",
            "3" => "withoutresult",
            "4" => "pure",
            "5" => "decider-aws",
            "6" => "withoutresult-aws",
            _ => templateType
        };

        var validTypes = new[] { "decider", "dcb", "withoutresult", "pure", "decider-aws", "withoutresult-aws" };
        if (!validTypes.Contains(templateType))
        {
             Console.Error.WriteLine($"❌ Invalid template type: {templateType}. Allowed values are: {string.Join(", ", validTypes.Select(t => $"'{t}'"))}.");
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
        else if (templateType == "decider")
        {
             installCommand = "new install Sekiban.Dcb.Templates";
             createCommand = $"new sekiban-dcb-decider -n {projectName}";
        }
        else if (templateType == "decider-aws")
        {
             installCommand = "new install Sekiban.Dcb.Templates";
             createCommand = $"new sekiban-dcb-decider-aws -n {projectName}";
        }
        else if (templateType == "withoutresult")
        {
             installCommand = "new install Sekiban.Dcb.Templates";
             createCommand = $"new sekiban-dcb-orleans-withoutresult -n {projectName}";
        }
        else if (templateType == "withoutresult-aws")
        {
             installCommand = "new install Sekiban.Dcb.Templates";
             createCommand = $"new sekiban-dcb-orleans-aws -n {projectName}";
        }
        else // dcb
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
        Console.WriteLine("  -t, --type   The type of template to use. Default is 'decider'.");
        Console.WriteLine("               Available types:");
        Console.WriteLine("                 decider          - Sekiban Dcb Decider Aspire Template (recommended)");
        Console.WriteLine("                 dcb              - Sekiban Dcb Orleans Aspire Template");
        Console.WriteLine("                 withoutresult    - Sekiban Dcb Orleans Aspire Template (WithoutResult)");
        Console.WriteLine("                 pure             - Sekiban Pure Orleans Aspire Template");
        Console.WriteLine("                 decider-aws      - Sekiban Dcb Decider AWS Template (DynamoDB)");
        Console.WriteLine("                 withoutresult-aws - Sekiban Dcb Orleans AWS Template (DynamoDB)");
        Console.WriteLine("  -h, --help   Show this help message.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  create-sekiban-dcb-template MyProject");
        Console.WriteLine("  create-sekiban-dcb-template MyProject -t decider");
        Console.WriteLine("  create-sekiban-dcb-template MyProject -t dcb");
        Console.WriteLine("  create-sekiban-dcb-template MyProject -t withoutresult");
        Console.WriteLine("  create-sekiban-dcb-template MyProject -t pure");
        Console.WriteLine("  create-sekiban-dcb-template MyProject -t decider-aws");
        Console.WriteLine("  create-sekiban-dcb-template MyProject -t withoutresult-aws");
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
