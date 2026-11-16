using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Simple in-memory registry for multi projectors.
/// </summary>
internal class SimpleMultiProjectorTypes : SimpleMultiProjectorTypesBase
{
    /// <summary>
    ///     Register a multi projector type using its static GetMultiProjectorName
    /// </summary>
    public void RegisterProjector<TProjector>() where TProjector : ICoreMultiProjector<TProjector>, new() =>
        RegisterCoreProjector<TProjector>();

    /// <summary>
    ///     Register a multi projector type using its static GetMultiProjectorName.
    ///     The core projector contract is exception based, so this is the same as <see cref="RegisterProjector{TProjector}" />.
    /// </summary>
    public void RegisterProjectorWithoutResult<TProjector>() where TProjector : ICoreMultiProjector<TProjector>, new() =>
        RegisterCoreProjector<TProjector>();

    /// <summary>
    ///     Registers a projector with custom serialization support.
    ///     The projector must implement IMultiProjectorWithCustomSerialization interface.
    /// </summary>
    public bool RegisterProjectorWithCustomSerialization<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
        RegisterCoreProjectorWithCustomSerialization<T>();

    /// <summary>
    ///     Registers a projector with custom serialization support.
    ///     The core projector contract is exception based, so this is the same as
    ///     <see cref="RegisterProjectorWithCustomSerialization{T}" />.
    /// </summary>
    public bool RegisterProjectorWithCustomSerializationWithoutResult<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
        RegisterCoreProjectorWithCustomSerialization<T>();

    /// <summary>
    ///     Core registers a custom-serialized projector through <see cref="RegisterProjector{TProjector}" /> (so a
    ///     conflicting registration is rejected) and then installs its serializers.
    /// </summary>
    protected override bool RegisterCoreProjectorWithCustomSerialization<T>()
    {
        RegisterCoreProjector<T>();
        return base.RegisterCoreProjectorWithCustomSerialization<T>();
    }
}
