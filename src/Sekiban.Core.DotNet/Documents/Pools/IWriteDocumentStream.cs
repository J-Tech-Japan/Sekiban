using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Documents.Pools;

public interface IWriteDocumentStream
{
    public AggregateContainerGroup GetAggregateContainerGroup();
}
