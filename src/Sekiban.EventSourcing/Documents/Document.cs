using Newtonsoft.Json;
namespace Sekiban.EventSourcing.Documents;

public record Document
{

    private string _partitionKey = string.Empty;
    [JsonProperty("id")]
    public Guid Id { get; init; }
    [JsonProperty("partitionkey")]
    public string PartitionKey
    {
        get => _partitionKey;
        init => _partitionKey = value;
    }

    public DocumentType DocumentType { get; init; }
    public string DocumentTypeName { get; init; } = null!;
    public DateTime TimeStamp { get; init; }
    public string SortableUniqueId { get; private set; } = string.Empty;

    public Document() { }

    public Document(DocumentType documentType, IPartitionKeyFactory? partitionKeyFactory, string? documentTypeName = null)
    {
        Id = Guid.NewGuid();
        DocumentType = documentType;
        DocumentTypeName = documentTypeName ?? GetType().Name;
        TimeStamp = DateTime.UtcNow;
        if (partitionKeyFactory is not null)
        {
            SetPartitionKey(partitionKeyFactory);
        }
        UpdateSortableUniqueId();
    }

    public void UpdateSortableUniqueId()
    {
        SortableUniqueId = DateTime.UtcNow.Ticks + (Math.Abs(Id.GetHashCode()) % 1000000000000).ToString("000000000000");
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
