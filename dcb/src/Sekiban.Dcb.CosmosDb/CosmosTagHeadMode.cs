namespace Sekiban.Dcb.CosmosDb;

/// <summary>Controls maintenance of the per-tag maximum; no mode enforces a fence.</summary>
public enum CosmosTagHeadMode
{
    /// <summary>Existing row-only writes.</summary>
    Off = 0,
    /// <summary>Maintain a monotonic tag head before rows become visible.</summary>
    Advance = 1
}
