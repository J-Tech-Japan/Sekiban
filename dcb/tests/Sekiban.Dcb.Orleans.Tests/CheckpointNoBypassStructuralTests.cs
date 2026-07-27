using System.Text.RegularExpressions;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G20 structural no-bypass guard. Every product path that mutates the external checkpoint store must go through
///     the capability-aware coordinator (CAS on a capable store; legacy only as a marked non-capable fallback). This
///     scans the production source for ANY direct legacy write/delete on the checkpoint store
///     (<c>UpsertFromStreamAsync</c> / <c>UpsertAsync</c> / <c>DeleteAsync</c> / <c>DeleteAllAsync</c>) and fails unless
///     each call site is a documented <c>LEGACY-FALLBACK</c> branch. A new unconditional bypass — admin delete,
///     StateBuilder, inline/streaming/version-rewrite/rebuild — trips this test at author time, not by luck of a runtime
///     path being hit.
/// </summary>
public class CheckpointNoBypassStructuralTests
{
    private static readonly Regex LegacyMutation = new(
        @"_(multiProjectionStateStore|stateStore)\s*!?\.\s*(UpsertFromStreamAsync|UpsertAsync|DeleteAsync|DeleteAllAsync)\s*\(",
        RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sekiban.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Sekiban.slnx).");
    }

    [Theory]
    [InlineData("dcb/src/Sekiban.Dcb.Orleans.Core/Grains/MultiProjectionGrain.cs")]
    [InlineData("dcb/src/Sekiban.Dcb.Core/MultiProjections/MultiProjectionStateBuilder.cs")]
    public void EveryDirectLegacyCheckpointMutation_IsAMarkedNonCapableFallback(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), relativePath);
        Assert.True(File.Exists(path), $"source not found: {path}");
        var lines = File.ReadAllLines(path);

        var offenders = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!LegacyMutation.IsMatch(lines[i]))
            {
                continue;
            }
            // A LEGACY-FALLBACK sentinel must appear within the preceding 6 lines (the marked non-capable branch).
            var window = string.Join('\n', lines.Skip(Math.Max(0, i - 6)).Take(7));
            if (!window.Contains("LEGACY-FALLBACK", StringComparison.Ordinal))
            {
                offenders.Add($"{relativePath}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Unmarked direct legacy checkpoint mutation(s) — route through the capability-aware coordinator or mark the "
            + "non-capable branch LEGACY-FALLBACK:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void TheGuard_IsNonVacuous_ItActuallyFindsLegacyMutations()
    {
        // Sanity: the scan must match at least the two known (marked) legacy fallbacks, or the regex has rotted and the
        // guard would pass vacuously.
        var total = 0;
        foreach (var rel in new[]
        {
            "dcb/src/Sekiban.Dcb.Orleans.Core/Grains/MultiProjectionGrain.cs",
            "dcb/src/Sekiban.Dcb.Core/MultiProjections/MultiProjectionStateBuilder.cs"
        })
        {
            total += File.ReadAllLines(Path.Combine(RepoRoot(), rel)).Count(l => LegacyMutation.IsMatch(l));
        }
        Assert.True(total >= 2, $"the legacy-mutation scan matched {total} lines — expected at least the known fallbacks");
    }
}
