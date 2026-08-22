using Sekiban.Dcb.Actors;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     The failure a query gets when the projection it needs is stuck on a poison event.
///     It is what a faulted projection returns instead of a stale, successful-looking empty state — and it names the
///     fault: which event, which projector, which position. The full <see cref="ProjectionFaultDescriptor" /> is on
///     <see cref="Fault" />, and its identifiers are also on <see cref="System.Exception.Data" /> under the same keys
///     SEK-G9's boundaries use, so it reads consistently wherever it surfaces.
///     This is used when the fault has to be RE-raised from a stored descriptor — an Orleans activation that restored
///     a persisted fault, for instance — where the original exception instance no longer exists. Where the original
///     instance IS still in hand (the in-process fold path), that instance is rethrown directly instead, annotated
///     with the same data; this type does not replace it.
/// </summary>
public sealed class SekibanProjectionFaultException : Exception
{
    /// <summary>Creates the exception from a fault descriptor.</summary>
    public SekibanProjectionFaultException(ProjectionFaultDescriptor fault)
        : base((fault ?? throw new ArgumentNullException(nameof(fault))).DescribeReRaise())
    {
        Fault = fault;
        // A constructor is a public reconstruction boundary too. Keep every descriptor-derived annotation here so a
        // caller never gets a lookalike first-fault exception merely because it did not pass through Orleans.
        Fault.AnnotateReRaise(this);
    }

    /// <summary>Everything known about the fault: event id, type, projector, position, message, time.</summary>
    public ProjectionFaultDescriptor Fault { get; }

    /// <summary>
    ///     Always <see langword="true" />: this type is used only when a persisted descriptor is reconstructed. The
    ///     original exception instance from the first fold is rethrown directly and never receives this marker.
    /// </summary>
    public bool IsReRaise => true;
}
