namespace Sekiban.Dcb.Capabilities;

/// <summary>
///     Thrown at startup when a Production host is composed in a way that would lose data, and the host is stopped
///     before it can serve a single request.
///     This is the whole point of the guard: the failure it describes is not a failure the running system would ever
///     have reported. A volatile store accepts every write and answers every read; a testing executor executes every
///     command. Nothing looks wrong. It looks wrong only later, when the process restarts and the events are gone.
/// </summary>
public sealed class SekibanDcbProductionGuardException : Exception
{
    /// <summary>Creates the exception with the message the operator will see in the crash log.</summary>
    public SekibanDcbProductionGuardException(string message, SekibanDcbCapabilityReport report) : base(message) =>
        Report = report;

    /// <summary>Everything the guard resolved and asked, so the message never has to be the only evidence.</summary>
    public SekibanDcbCapabilityReport Report { get; }
}
