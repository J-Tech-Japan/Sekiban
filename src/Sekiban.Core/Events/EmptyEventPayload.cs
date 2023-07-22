namespace Sekiban.Core.Events;

/// <summary>
///     represents empty event payload.
///     App developers does not need to use this class.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public record EmptyEventPayload : IEventPayloadCommon;
