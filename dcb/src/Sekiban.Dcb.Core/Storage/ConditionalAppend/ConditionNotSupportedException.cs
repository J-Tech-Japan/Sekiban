using Sekiban.Dcb.Capabilities;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     The typed intentional failure for <see cref="ConditionalAppendStatus.ConditionNotSupported" />: the actually
///     resolved event store cannot enforce the requested <see cref="WriteConditionKind" />. Raised FAIL-CLOSED — before
///     the command handler runs, before any EventId is allocated, before serialization, and before any store call — so a
///     store that cannot guarantee the condition never silently degrades to an unconditional write. Following the G9/G14
///     contract it is <c>ResultBox.Error</c> on WithResult and thrown by the WithoutResult guarded boundary.
/// </summary>
public sealed class ConditionNotSupportedException : Exception
{
    public ConditionNotSupportedException(WriteConditionKind requestedKind, string providerName)
        : base(
            $"The resolved event store '{providerName}' does not support the requested write condition "
            + $"'{requestedKind}'. Conditional append fails closed rather than degrade to an unconditional write.")
    {
        RequestedKind = requestedKind;
        ProviderName = providerName;
    }

    public ConditionalAppendStatus Status => ConditionalAppendStatus.ConditionNotSupported;
    public WriteConditionKind RequestedKind { get; }
    public string ProviderName { get; }
}
