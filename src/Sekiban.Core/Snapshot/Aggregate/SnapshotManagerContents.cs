using Sekiban.Core.Aggregate;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Snapshot.Aggregate;

public record SnapshotManagerContents : IAggregateContents
{
    public IReadOnlyCollection<string> Requests { get; set; } = new List<string>();
    public IReadOnlyCollection<string> RequestTakens { get; set; } = new List<string>();
    public DateTime CreatedAt { get; set; } = SekibanDateProducer.GetRegistered().UtcNow;
}
