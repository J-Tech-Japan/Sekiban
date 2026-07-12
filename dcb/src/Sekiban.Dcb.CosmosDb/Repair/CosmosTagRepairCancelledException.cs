namespace Sekiban.Dcb.CosmosDb.Repair;

/// <summary>
///     Thrown when a repair run is cancelled, carrying the progress it had already settled.
///     Cancelling a bounded scan does not undo the events it finished — and throwing the progress away with
///     the exception is worse than useless: a caller with a tight budget would restart from the same place
///     every turn and never advance. So cancellation still cancels (this IS an
///     <see cref="OperationCanceledException" />, and code that catches one keeps working), but the partial
///     report comes with it, including a <see cref="CosmosTagRepairReport.Checkpoint" /> that resumes after
///     the last event the run fully settled.
/// </summary>
public class CosmosTagRepairCancelledException : OperationCanceledException
{
    /// <summary>
    ///     Creates a cancellation exception.
    /// </summary>
    public CosmosTagRepairCancelledException()
    {
    }

    /// <summary>
    ///     Creates a cancellation exception with a message.
    /// </summary>
    public CosmosTagRepairCancelledException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Creates a cancellation exception with a message and inner exception.
    /// </summary>
    public CosmosTagRepairCancelledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     Creates a cancellation exception carrying the progress the run had settled.
    /// </summary>
    public CosmosTagRepairCancelledException(CosmosTagRepairReport partialReport, CancellationToken cancellationToken)
        : base(
            $"The repair run was cancelled after settling {partialReport?.EventsScanned ?? 0} event(s). " +
            "Its partial progress is on this exception; resume from its checkpoint.",
            cancellationToken) =>
        PartialReport = partialReport ?? new CosmosTagRepairReport();

    /// <summary>
    ///     What the run had settled when it was cancelled. Its <see cref="CosmosTagRepairReport.Checkpoint" />
    ///     resumes after the last event fully classified and repaired — never mid-event, so resuming re-reads
    ///     nothing it already wrote.
    /// </summary>
    public CosmosTagRepairReport PartialReport { get; } = new();
}
