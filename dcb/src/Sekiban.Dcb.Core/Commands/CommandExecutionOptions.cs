namespace Sekiban.Dcb.Commands;

/// <summary>
///     Opt-in options for a command execution. Passed only through the NEW executor overloads; the existing
///     <c>ExecuteAsync</c> signatures do not take it, so default behaviour is byte-for-byte unchanged. Absent (or with
///     every field null) it means "execute exactly as before".
/// </summary>
public sealed record CommandExecutionOptions
{
    /// <summary>
    ///     When set, the command's single appended event is written through the store's conditional (unique-key) append
    ///     path instead of the unconditional path. The resolved store MUST support
    ///     <c>WriteConditionKind.SingleEventUniqueKey</c> or execution fails closed with
    ///     <c>ConditionNotSupportedException</c> before the handler runs.
    /// </summary>
    public ConditionalAppendSpecification? ConditionalAppend { get; init; }
}

/// <summary>
///     Requests single-event conditional append under a caller-supplied idempotency key. The command handler must append
///     exactly one event (the single-event contract); zero or more-than-one appended events is an error.
/// </summary>
public sealed record ConditionalAppendSpecification(string IdempotencyKey);
