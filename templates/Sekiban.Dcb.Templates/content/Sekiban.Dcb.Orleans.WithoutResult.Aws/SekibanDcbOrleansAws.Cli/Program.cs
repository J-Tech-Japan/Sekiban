using System.CommandLine;
using Dcb.Domain.WithoutResult;
using Sekiban.Dcb;

// Create the root command
var rootCommand = new RootCommand(
@"Sekiban DCB CLI for AWS DynamoDB - Utility commands

This CLI provides basic utility commands for managing your Sekiban DCB application
with DynamoDB storage.

Commands:
  list    List all registered projectors and tag groups

Examples:
  # List all registered projectors and tag groups
  dotnet run -- list
");

// List command
var listCommand = new Command("list", "List all registered projectors and tag groups");
listCommand.SetAction(async (parseResult, cancellationToken) => await ListProjectorsAsync());

rootCommand.Subcommands.Add(listCommand);

return await rootCommand.Parse(args).InvokeAsync();

static async Task ListProjectorsAsync()
{
    Console.WriteLine("=== Registered Projectors ===\n");

    var domainTypes = DomainType.GetDomainTypes();
    var projectorNames = domainTypes.MultiProjectorTypes.GetAllProjectorNames();

    Console.WriteLine($"Total: {projectorNames.Count} projector(s)\n");

    foreach (var name in projectorNames)
    {
        var versionResult = domainTypes.MultiProjectorTypes.GetProjectorVersion(name);
        var version = versionResult.IsSuccess ? versionResult.GetValue() : "unknown";
        Console.WriteLine($"  - {name} (version: {version})");
    }

    Console.WriteLine("\n=== Registered Tag Projectors ===\n");
    var tagProjectorNames = domainTypes.TagProjectorTypes.GetAllProjectorNames();
    Console.WriteLine($"Total: {tagProjectorNames.Count} tag projector(s)\n");

    foreach (var name in tagProjectorNames)
    {
        var versionResult = domainTypes.TagProjectorTypes.GetProjectorVersion(name);
        var version = versionResult.IsSuccess ? versionResult.GetValue() : "unknown";
        Console.WriteLine($"  - {name} (version: {version})");
    }

    Console.WriteLine("\n=== Registered Tag Groups ===\n");
    var tagGroupNames = domainTypes.TagTypes.GetAllTagGroupNames();
    Console.WriteLine($"Total: {tagGroupNames.Count} tag group(s)\n");

    foreach (var name in tagGroupNames)
    {
        Console.WriteLine($"  - {name}");
    }

    await Task.CompletedTask;
}
