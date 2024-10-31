using ResultBoxes;
using Sekiban.Core.Aggregate;
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
//TBD
