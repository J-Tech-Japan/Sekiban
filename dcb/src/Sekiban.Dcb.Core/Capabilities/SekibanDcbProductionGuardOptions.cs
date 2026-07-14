namespace Sekiban.Dcb.Capabilities;

/// <summary>
///     What the production guard is allowed to let through.
///     There is exactly one override here, and it is deliberately narrow. There is <b>no</b> override that authorises a
///     testing executor in Production — not off by default, not hidden behind a flag: it does not exist. A volatile
///     store in Production is a decision an operator can make (a cache-shaped service, a throwaway environment named
///     Production). A test executor in Production is never a decision, it is an accident.
/// </summary>
public sealed class SekibanDcbProductionGuardOptions
{
    /// <summary>
    ///     Permits a <see cref="StorageDurability.Volatile" /> or <see cref="StorageDurability.Unknown" /> store in
    ///     Production. <b>Storage only.</b> It says nothing about the executor, and the guard will not let it: an
    ///     environment with this set and a testing executor still fails closed.
    ///     Default false. If you set it, the banner says so, by name, at Warning.
    /// </summary>
    public bool AllowVolatileStorageInProduction { get; set; }

    /// <summary>
    ///     The environment names the guard treats as Production. Defaults to ASP.NET Core's own "Production".
    ///     Add to it if your real environments are named something else ("Staging" that is really production, "Prod").
    ///     Names are compared case-insensitively.
    /// </summary>
    public IList<string> ProductionEnvironmentNames { get; } = new List<string> { "Production" };

    /// <summary>The override names that were actually used, for the banner. Empty when none were.</summary>
    internal IReadOnlyList<string> UsedOverrideNames() =>
        AllowVolatileStorageInProduction ? [nameof(AllowVolatileStorageInProduction)] : [];
}
