using System.Diagnostics;

namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     Keeps the dependency-free JavaScript witness on the same CI path as the C# test project. GitHub's DCB jobs run
///     this project for net9.0 and net10.0, so the Node half cannot silently become an unexecuted documentation script.
/// </summary>
public sealed class SerializedCommitInteropNodeRunnerTests
{
    [Fact]
    public void NodeRunner_ExecutesEveryFrozenFixtureFromTheCommittedCheckout()
    {
        var result = RunNode();

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("SEK-G52 Node interop runner passed 15 frozen fixtures.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeRunner_RejectsAProvenanceDigestMismatch()
    {
        var root = SerializedCommitInteropPaths.FindRepositoryRoot();
        var fixture = Path.Combine(
            root,
            "dcb",
            "tests",
            "Sekiban.Dcb.WithResult.Tests",
            "SerializedCommitWire",
            "goldens",
            "interop_official_v1_populated.json");
        var result = RunNode("--verify-file", fixture, "--expected-sha", new string('0', 64));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SHA-256 mismatch", result.Output, StringComparison.Ordinal);
    }

    private static ProcessResult RunNode(params string[] arguments)
    {
        var root = SerializedCommitInteropPaths.FindRepositoryRoot();
        var script = Path.Combine(
            root,
            "dcb",
            "tests",
            "Sekiban.Dcb.WithResult.Tests",
            "SerializedCommitWire",
            "serialized_commit_interop_runner.mjs");
        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(script);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start node.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout + stderr);
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}

internal static class SerializedCommitInteropPaths
{
    internal static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Sekiban.slnx")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "dcb")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Sekiban repository root for frozen interop fixtures.");
    }
}
