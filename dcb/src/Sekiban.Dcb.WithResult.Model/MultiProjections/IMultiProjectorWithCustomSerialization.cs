namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Interface for multi-projectors that require custom serialization logic (ResultBox-based error handling).
///     Projection follows <see cref="IMultiProjector{T}" />; serialization follows
///     <see cref="ICoreMultiProjectorWithCustomSerialization{TSelf}" />.
/// </summary>
public interface IMultiProjectorWithCustomSerialization<TSelf> : ICoreMultiProjectorWithCustomSerialization<TSelf>,
    IMultiProjector<TSelf>
    where TSelf : IMultiProjectorWithCustomSerialization<TSelf>
{
}
