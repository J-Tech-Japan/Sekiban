using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Simple in-memory registry for multi projectors.
/// </summary>
public class SimpleMultiProjectorTypes : SimpleMultiProjectorTypesBase, IMultiProjectorTypes
{
    /// <summary>
    ///     Register a multi projector type using its static GetMultiProjectorName.
    ///     The projector's ResultBox is unwrapped once at the core projector contract.
    /// </summary>
    public void RegisterProjector<TProjector>() where TProjector : IMultiProjector<TProjector>, new() =>
        RegisterCoreProjector<TProjector>();

    /// <summary>
    ///     Register a multi projector type using its static GetMultiProjectorName.
    ///     Same as <see cref="RegisterProjector{TProjector}" />.
    /// </summary>
    public void RegisterProjectorWithoutResult<TProjector>() where TProjector : IMultiProjector<TProjector>, new() =>
        RegisterCoreProjector<TProjector>();

    /// <summary>
    ///     Registers a projector with custom serialization support.
    ///     The projector must implement IMultiProjectorWithCustomSerialization interface.
    /// </summary>
    public ResultBox<bool> RegisterProjectorWithCustomSerialization<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
        ResultBox.WrapTry(RegisterCoreProjectorWithCustomSerialization<T>);

    /// <summary>
    ///     Registers a projector with custom serialization support.
    ///     The projector must implement IMultiProjectorWithCustomSerialization interface.
    /// </summary>
    public ResultBox<bool> RegisterProjectorWithCustomSerializationWithoutResult<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
        ResultBox.WrapTry(RegisterCoreProjectorWithCustomSerialization<T>);
}
