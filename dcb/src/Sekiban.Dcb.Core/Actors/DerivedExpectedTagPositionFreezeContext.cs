using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     Internal test-only context for the derived expected-position freeze barrier. It carries the real command
///     context and collected event instances; no public callback or production dependency is introduced.
/// </summary>
internal sealed record DerivedExpectedTagPositionFreezeContext(
    CoreGeneralCommandContext CommandContext,
    IReadOnlyList<EventPayloadWithTags> CollectedEvents);
