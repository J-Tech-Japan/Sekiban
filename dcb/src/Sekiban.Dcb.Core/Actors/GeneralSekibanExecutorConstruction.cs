namespace Sekiban.Dcb.Actors;

/// <summary>
///     Internal construction state shared by the ResultBox and exception facade assemblies.
///     Keeping the state transfer here lets both public facades retain their historical constructors without
///     duplicating the size-gate construction bodies.
/// </summary>
internal sealed class GeneralSekibanExecutorConstruction
{
    internal GeneralSekibanExecutorConstruction(
        IActorObjectAccessor actorAccessor,
        CoreGeneralSekibanExecutor core)
    {
        ActorAccessor = actorAccessor;
        Core = core;
    }

    internal IActorObjectAccessor ActorAccessor { get; }

    internal CoreGeneralSekibanExecutor Core { get; }
}
