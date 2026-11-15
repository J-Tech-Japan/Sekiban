using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Simple in-memory registry for multi projectors.
/// </summary>
public class SimpleMultiProjectorTypes : SimpleMultiProjectorTypesBase, IMultiProjectorTypes
{
    /// <summary>
    ///     Register a multi projector type using its static GetMultiProjectorName
    /// </summary>
    public void RegisterProjector<TProjector>() where TProjector : IMultiProjector<TProjector>, new() =>
        RegisterProjectorEntry<TProjector>(
            TProjector.MultiProjectorName,
            TProjector.MultiProjectorVersion,
            () => TProjector.GenerateInitialPayload(),
            ProjectAs<TProjector>(TProjector.Project));

    /// <summary>
    ///     Registers a projector with custom serialization support (exception-based version).
    ///     The projector must implement IMultiProjectorWithCustomSerialization interface.
    /// </summary>
    public ResultBox<bool> RegisterProjectorWithCustomSerialization<T>()
        where T : IMultiProjectorWithCustomSerialization<T>, new() =>
        ResultBox.WrapTry(() => RegisterCustomSerializedProjector<T>(
            T.MultiProjectorName,
            T.MultiProjectorVersion,
            () => T.GenerateInitialPayload(),
            ProjectAs<T>(T.Project),
            (domain, safeWindowThreshold, payload) => T.Serialize(domain, safeWindowThreshold, (T)payload),
            (domain, safeWindowThreshold, data) => T.Deserialize(domain, safeWindowThreshold, data)));
}
