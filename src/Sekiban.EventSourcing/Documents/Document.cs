using Newtonsoft.Json;
using System.Runtime.Serialization;
namespace Sekiban.EventSourcing.Documents;

public record Document
{

    private string _partitionKey = string.Empty;
    [JsonProperty("id")] [DataMember]

    public Guid Id { get; init; }
    [JsonProperty("partitionkey")]
    [DataMember]
    public string PartitionKey
    {
        get => _partitionKey;
        init => _partitionKey = value;
    }

    [DataMember]
    public DocumentType DocumentType { get; init; }
    [DataMember]
    public string DocumentTypeName { get; init; } = null!;
    [DataMember]
    public DateTime TimeStamp { get; init; }
    [DataMember]
    public string SortableUniqueId { get; init; } = string.Empty;

    public Document(DocumentType documentType, IPartitionKeyFactory? partitionKeyFactory, string? documentTypeName = null)
    {
        Id = Guid.NewGuid();
        DocumentType = documentType;
        DocumentTypeName = documentTypeName ?? GetType().Name;
        TimeStamp = DateTime.UtcNow;
        SortableUniqueId = TimeStamp.Ticks + (Math.Abs(Id.GetHashCode()) % 1000000000000).ToString("000000000000");
        if (partitionKeyFactory is not null)
        {
            SetPartitionKey(partitionKeyFactory);
        }
    }

    public void SetPartitionKey(IPartitionKeyFactory partitionKeyFactory)
    {
        if (partitionKeyFactory is CanNotUsePartitionKeyFactory)
        {
            return;
        }
        _partitionKey = partitionKeyFactory.GetPartitionKey(DocumentType);
    }

    public override string ToString() =>
        JsonConvert.SerializeObject(this);
}
