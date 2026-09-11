using System.Diagnostics;

namespace Sekiban.Dcb.TemplateValidation;

internal static class ValidationProcessSupport
{
    internal const string DirectoryBuildPropsFileName = "Directory.Build.props";

    internal static string ResolveImportPath(string csprojPath, string projectAttribute)
    {
        var projectDirectory = Path.GetDirectoryName(csprojPath)! + Path.DirectorySeparatorChar;
        var expanded = projectAttribute
            .Replace("$(MSBuildThisFileDirectory)", projectDirectory, StringComparison.Ordinal)
            .Replace('\\', Path.DirectorySeparatorChar);
        if (expanded.Contains("$(", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Import '{projectAttribute}' in {csprojPath} does not resolve without an ambient property.");
        }

        return Path.GetFullPath(Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(projectDirectory, expanded));
    }

    internal static ProcessResult RunProcess(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout + stderr);
    }

    internal sealed record ProcessResult(int ExitCode, string Output);
}
