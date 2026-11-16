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
    ///     Returns true once the projector is registered; registering the same projector name again also returns
    ///     true and installs its serializers. A failure is thrown as the exception that caused it.
    /// </summary>
    public bool RegisterProjectorWithCustomSerialization<T>()
        where T : IMultiProjectorWithCustomSerialization<T>, new() =>
        RegisterCustomSerializedProjector<T>(
            T.MultiProjectorName,
            T.MultiProjectorVersion,
            () => T.GenerateInitialPayload(),
            ProjectAs<T>(T.Project),
            (domain, safeWindowThreshold, payload) => T.Serialize(domain, safeWindowThreshold, (T)payload),
            (domain, safeWindowThreshold, data) => T.Deserialize(domain, safeWindowThreshold, data));

    /// <summary>
    ///     The core registration keeps its own constraint. Restated here because this class re-implements
    ///     <see cref="ICoreMultiProjectorTypes" /> through <see cref="IMultiProjectorTypes" />, and the public method
    ///     above has the same shape with a narrower constraint.
    /// </summary>
    bool ICoreMultiProjectorTypes.RegisterProjectorWithCustomSerialization<T>() =>
        RegisterCoreProjectorWithCustomSerialization<T>();
}
