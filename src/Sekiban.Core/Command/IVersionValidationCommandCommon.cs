namespace Sekiban.Core.Command;

/// <summary>
///     System use for add command validation feature
///     Application developer does not need to implement this interface directly,
///     instead, use <see cref="IVersionValidationCommand" /> to add validation feature
/// </summary>
public interface IVersionValidationCommandCommon
{
    /// <summary>
    ///     Aggregate Version for the
    /// </summary>
    public int ReferenceVersion { get; init; }
}
