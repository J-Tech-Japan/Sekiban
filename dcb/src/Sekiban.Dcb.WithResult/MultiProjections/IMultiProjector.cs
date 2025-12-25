namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Alias for ICoreMultiProjector in WithResult package.
///     Generic interface for multi projectors with static members (ResultBox-based error handling).
/// </summary>
public interface IMultiProjector<T> : ICoreMultiProjector<T> where T : IMultiProjector<T>
{
}
