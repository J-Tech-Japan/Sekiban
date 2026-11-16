using System.Xml.Linq;
namespace Sekiban.Dcb.MaterializedView.ApiSurfaceVerifier;

/// <summary>
///     Public API of the 10.14.1 baseline that the 11.0 line removes on purpose. Every entry is the surface key the
///     verifier reads, paired with the CompatibilitySuppressions.xml target that records the same break against the
///     10.25.0 package baseline. Any other removal still fails, and an entry that is no longer removed fails too.
/// </summary>
internal static class IntendedRemovals
{
    internal static readonly IReadOnlyList<(string SurfaceKey, string SuppressionTarget)> Entries =
    [
        // DCB 11 slice 4: MaterializedView no longer depends on ResultBoxes; the protected catch-up helper takes the
        // read events (and a separate overload the read failure) instead of a ResultBox.
        ("Sekiban.Dcb.MaterializedView.MvExecutorBase`1::method:CompleteCatchUpAsync`0:System.Threading.Tasks.Task`1[[Sekiban.Dcb.MaterializedView.MvCatchUpResult, Sekiban.Dcb.MaterializedView, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]]:Sekiban.Dcb.MaterializedView.IMvApplyHost:None,System.String:None,ResultBoxes.ResultBox`1[[System.Collections.Generic.IEnumerable`1[[Sekiban.Dcb.Events.SerializableEvent, Sekiban.Dcb.Core, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]], System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]:None,Sekiban.Dcb.MaterializedView.MvProjectionStatusSnapshot:None,System.Threading.CancellationToken:None",
            "M:Sekiban.Dcb.MaterializedView.MvExecutorBase`1.CompleteCatchUpAsync(Sekiban.Dcb.MaterializedView.IMvApplyHost,System.String,ResultBoxes.ResultBox{System.Collections.Generic.IEnumerable{Sekiban.Dcb.Events.SerializableEvent}},Sekiban.Dcb.MaterializedView.MvProjectionStatusSnapshot,System.Threading.CancellationToken)")
    ];

    /// <summary>
    ///     Returns the removals that are not intended. Throws when an intended removal did not happen, or when the
    ///     suppression file (found next to the source when the verifier runs from the repository root) does not record
    ///     an intended removal.
    /// </summary>
    internal static IReadOnlyList<string> Unexpected(IReadOnlyCollection<string> removed, string suppressionFilePath)
    {
        var intended = Entries.Select(entry => entry.SurfaceKey).ToHashSet(StringComparer.Ordinal);
        var missing = intended.Where(key => !removed.Contains(key)).ToList();
        if (missing.Count != 0)
        {
            throw new InvalidOperationException(
                $"An intended 11.0 removal is no longer removed; update the allow-list:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
        }

        if (File.Exists(suppressionFilePath))
        {
            var targets = XDocument.Load(suppressionFilePath).Descendants("Target").Select(element => element.Value)
                .ToHashSet(StringComparer.Ordinal);
            var unrecorded = Entries.Where(entry => !targets.Contains(entry.SuppressionTarget)).ToList();
            if (unrecorded.Count != 0)
            {
                throw new InvalidOperationException(
                    "An intended 11.0 removal is not recorded in " + suppressionFilePath + ":" + Environment.NewLine +
                    string.Join(Environment.NewLine, unrecorded.Select(entry => entry.SuppressionTarget)));
            }
        }

        return removed.Where(key => !intended.Contains(key)).ToList();
    }
}
