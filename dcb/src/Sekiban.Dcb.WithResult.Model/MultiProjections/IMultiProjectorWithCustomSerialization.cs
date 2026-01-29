namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Alias for ICoreMultiProjectorWithCustomSerialization in WithResult package.
///     Interface for multi-projectors that require custom serialization logic (ResultBox-based error handling).
/// </summary>
public interface IMultiProjectorWithCustomSerialization<TSelf> : ICoreMultiProjectorWithCustomSerialization<TSelf>
    where TSelf : IMultiProjectorWithCustomSerialization<TSelf>
{
}
