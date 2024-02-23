using Sekiban.Core.Documents;
namespace Sekiban.Infrastructure.Postgres.Databases;

public interface IDbEvent
{
    public Guid Id { get; init; }
    public string Payload { get; init; }
    public int Version { get; init; }
    public string CallHistories { get; init; }
    public Guid AggregateId { get; init; }
    public string PartitionKey { get; init; }
    public DocumentType DocumentType { get; init; }
    public string DocumentTypeName { get; init; }
    public DateTime TimeStamp { get; init; }
    public string SortableUniqueId { get; init; }
    public string AggregateType { get; init; }
    public string RootPartitionKey { get; init; }
}