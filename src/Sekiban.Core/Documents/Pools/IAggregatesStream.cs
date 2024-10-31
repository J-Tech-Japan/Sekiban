using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Types;
namespace Sekiban.Core.Documents.Pools;

public interface IAggregatesStream
{
    public AggregateContainerGroup GetAggregateContainerGroup();
    public List<string> GetStreamNames();
    public ResultBox<string> GetSingleStreamName() =>
        ResultBox
            .UnitValue
            .Verify(
                () => GetStreamNames() is { Count : 1 } streamNames
                    ? ExceptionOrNone.None
                    : new ApplicationException("Stream Names is not set"))
            .Conveyor(_ => GetStreamNames()[0].ToResultBox());
}
public interface IWriteDocumentStream
{
    public AggregateContainerGroup GetAggregateContainerGroup();
}
public record AggregateWriteStream(Type AggregatePayloadType) : IWriteDocumentStream
{
    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(GetOriginal());
    public Type GetOriginal() => AggregatePayloadType.IsAggregatePayloadType()
        ? AggregatePayloadType.GetBaseAggregatePayloadTypeFromAggregate()
        : AggregatePayloadType;
}
//TBD
public record PureAggregateProjectionWriteStream(Type PureAggregateProjectionType) : IWriteDocumentStream
{
    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(PureAggregateProjectionType);
}
