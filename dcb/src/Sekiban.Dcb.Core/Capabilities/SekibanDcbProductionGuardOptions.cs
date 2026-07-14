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
    ///     Permits a store that declared itself <see cref="StorageDurability.Volatile" /> in Production.
    ///     It is narrow in two directions, and both are deliberate:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 <b>Storage only.</b> It says nothing about the executor. An environment with this set and a
    ///                 testing executor still fails closed.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 <b>Volatile only — never <see cref="StorageDurability.Unknown" />.</b> Setting this means "I
    ///                 looked at a store that said it was volatile, and I meant it". A store that says nothing has not
    ///                 given you anything to mean. Unknown stays fail-closed with this override on.
    ///             </description>
    ///         </item>
    ///     </list>
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
