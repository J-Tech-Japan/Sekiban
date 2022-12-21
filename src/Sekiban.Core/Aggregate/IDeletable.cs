namespace Sekiban.Core.Aggregate;

/// <summary>
///     System use interface to mark the entity is deletable.
///     Application Developer does not need to implement this interface
/// </summary>
public interface IDeletable
{
    public bool IsDeleted { get; }
}
