namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception throws when Version that in Command does not match with Version in Aggregate
/// </summary>
public class SekibanCommandInconsistentVersionException : Exception, ISekibanException
{
    /// <summary>
    ///     Aggregate Id
    /// </summary>
    public Guid AggregateId { get; }
    /// <summary>
    ///     The version that was in Aggregate
    /// </summary>
    public int CorrectVersion { get; }
    /// <summary>
    ///     The version that was in Aggregate
    /// </summary>
    public int PassedVersion { get; }
    /// <summary>
    ///     the root Partition Key
    /// </summary>
    public string RootPartitionKey { get; }

    public SekibanCommandInconsistentVersionException(Guid aggregateId, int passedVersion, int correctVersion, string rootPartitionKey) : base(
        $"for aggregate {aggregateId} and {rootPartitionKey} : {passedVersion} was passed but should be {correctVersion}")
    {
        AggregateId = aggregateId;
        CorrectVersion = correctVersion;
        RootPartitionKey = rootPartitionKey;
        PassedVersion = passedVersion;
    }
}
