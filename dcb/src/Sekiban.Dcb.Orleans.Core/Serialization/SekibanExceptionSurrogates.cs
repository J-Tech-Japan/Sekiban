using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.Orleans.Serialization;

// Orleans serializes System exceptions itself, but it has no codec for an exception type it does not know: such a
// failure reaches the caller as a codec error without its message. These converters let the Sekiban exception types
// that callers branch on cross a grain boundary with their type, message and the properties callers read. Each
// exception is rebuilt through its own constructor on the far side, so its message is the one it was raised with.
// The stack trace does not travel.

/// <summary>Carries an <see cref="ExpectedTagPositionConflictException" /> across a grain boundary.</summary>
[GenerateSerializer]
public struct ExpectedTagPositionConflictExceptionSurrogate
{
    [Id(0)] public List<TagHeadExpectedObservedSurrogate>? Pairs { get; set; }
}

/// <summary>One expected/observed pair of an <see cref="ExpectedTagPositionConflictException" />.</summary>
[GenerateSerializer]
public struct TagHeadExpectedObservedSurrogate
{
    [Id(0)] public string ServiceId { get; set; }
    [Id(1)] public string Tag { get; set; }
    [Id(2)] public TagHeadExpectationKind ExpectedKind { get; set; }
    [Id(3)] public string? ExpectedPosition { get; set; }
    [Id(4)] public string? ObservedPosition { get; set; }
}

[RegisterConverter]
public sealed class ExpectedTagPositionConflictExceptionConverter
    : IConverter<ExpectedTagPositionConflictException, ExpectedTagPositionConflictExceptionSurrogate>
{
    public ExpectedTagPositionConflictException ConvertFromSurrogate(
        in ExpectedTagPositionConflictExceptionSurrogate surrogate) =>
        new((surrogate.Pairs ?? [])
            .Select(pair => new TagHeadExpectedObserved(
                pair.ServiceId ?? string.Empty,
                pair.Tag ?? string.Empty,
                new TagHeadExpectation(pair.ExpectedKind, pair.ExpectedPosition),
                pair.ObservedPosition))
            .ToList());

    public ExpectedTagPositionConflictExceptionSurrogate ConvertToSurrogate(
        in ExpectedTagPositionConflictException value) =>
        new()
        {
            Pairs = (value.Pairs ?? [])
                .Select(pair => new TagHeadExpectedObservedSurrogate
                {
                    ServiceId = pair.ServiceId,
                    Tag = pair.Tag,
                    ExpectedKind = pair.Expected?.Kind ?? TagHeadExpectationKind.Unknown,
                    ExpectedPosition = pair.Expected?.Position,
                    ObservedPosition = pair.ObservedPosition
                })
                .ToList()
        };
}

/// <summary>Carries a <see cref="ConditionalAppendInDoubtException" /> across a grain boundary.</summary>
[GenerateSerializer]
public struct ConditionalAppendInDoubtExceptionSurrogate
{
    [Id(0)] public string ProviderName { get; set; }
    [Id(1)] public string ServiceId { get; set; }
    [Id(2)] public Guid DerivedEventId { get; set; }
    [Id(3)] public ConditionalAppendInDoubtReason Reason { get; set; }
    [Id(4)] public Exception? InnerException { get; set; }
}

[RegisterConverter]
public sealed class ConditionalAppendInDoubtExceptionConverter
    : IConverter<ConditionalAppendInDoubtException, ConditionalAppendInDoubtExceptionSurrogate>
{
    public ConditionalAppendInDoubtException ConvertFromSurrogate(in ConditionalAppendInDoubtExceptionSurrogate surrogate) =>
        ConditionalAppendInDoubtException.Create(
            surrogate.ProviderName ?? string.Empty,
            surrogate.ServiceId ?? string.Empty,
            surrogate.DerivedEventId,
            surrogate.Reason,
            surrogate.InnerException);

    public ConditionalAppendInDoubtExceptionSurrogate ConvertToSurrogate(in ConditionalAppendInDoubtException value) =>
        new()
        {
            ProviderName = value.ProviderName,
            ServiceId = value.ServiceId,
            DerivedEventId = value.DerivedEventId,
            Reason = value.Reason,
            InnerException = value.InnerException
        };
}

/// <summary>Carries a <see cref="KeyReuseConflictException" /> across a grain boundary.</summary>
[GenerateSerializer]
public struct KeyReuseConflictExceptionSurrogate
{
    [Id(0)] public string ExistingFingerprint { get; set; }
    [Id(1)] public string AttemptedFingerprint { get; set; }
    [Id(2)] public string ProviderName { get; set; }
    [Id(3)] public Exception? InnerException { get; set; }
}

[RegisterConverter]
public sealed class KeyReuseConflictExceptionConverter
    : IConverter<KeyReuseConflictException, KeyReuseConflictExceptionSurrogate>
{
    public KeyReuseConflictException ConvertFromSurrogate(in KeyReuseConflictExceptionSurrogate surrogate) =>
        new(
            surrogate.ExistingFingerprint ?? string.Empty,
            surrogate.AttemptedFingerprint ?? string.Empty,
            surrogate.ProviderName ?? string.Empty,
            surrogate.InnerException);

    public KeyReuseConflictExceptionSurrogate ConvertToSurrogate(in KeyReuseConflictException value) =>
        new()
        {
            ExistingFingerprint = value.ExistingFingerprint,
            AttemptedFingerprint = value.AttemptedFingerprint,
            ProviderName = value.ProviderName,
            InnerException = value.InnerException
        };
}

/// <summary>Carries a <see cref="TagHeadExpectationValidationException" /> across a grain boundary.</summary>
[GenerateSerializer]
public struct TagHeadExpectationValidationExceptionSurrogate
{
    [Id(0)] public string Message { get; set; }
}

[RegisterConverter]
public sealed class TagHeadExpectationValidationExceptionConverter
    : IConverter<TagHeadExpectationValidationException, TagHeadExpectationValidationExceptionSurrogate>
{
    public TagHeadExpectationValidationException ConvertFromSurrogate(
        in TagHeadExpectationValidationExceptionSurrogate surrogate) =>
        new(surrogate.Message ?? string.Empty);

    public TagHeadExpectationValidationExceptionSurrogate ConvertToSurrogate(
        in TagHeadExpectationValidationException value) =>
        new() { Message = value.Message };
}
