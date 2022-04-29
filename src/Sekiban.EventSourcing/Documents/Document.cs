using Newtonsoft.Json;
using Sekiban.EventSourcing.Partitions;

namespace Sekiban.EventSourcing.Documents;

public record Document
{
    [JsonProperty("id")]
    public Guid Id { get; init; }

    private string _partitionKey = string.Empty;
    [JsonProperty("partitionkey")]
    public string PartitionKey
    {
        get => _partitionKey;
        init => _partitionKey = value;
    }

    public DocumentType DocumentType { get; init; }
    public string DocumentTypeName { get; init; } = null!;
    public DateTime TimeStamp { get; init; }

    /// <summary>
    /// cosmosdb 保存時に自動設定されるtimestamp
    /// コードからは指定しない
    /// </summary>
    [JsonProperty("_ts")]
    public long Ts { get; init; }

    public Document() { }

    public Document(
        DocumentType documentType,
        IPartitionKeyFactory? partitionKeyFactory,
        string? documentTypeName = null)
    {
        Id = Guid.NewGuid();
        DocumentType = documentType;
        DocumentTypeName = documentTypeName ?? GetType().Name;
        TimeStamp = DateTime.UtcNow;

        if (partitionKeyFactory is not null)
            SetPartitionKey(partitionKeyFactory);
    }

    public void SetPartitionKey(IPartitionKeyFactory partitionKeyFactory)
    {
        if (partitionKeyFactory is CanNotUsePartitionKeyFactory)
            return;
        _partitionKey = partitionKeyFactory.GetPartitionKey(DocumentType);
    }

    public override string ToString() => JsonConvert.SerializeObject(this);
}
