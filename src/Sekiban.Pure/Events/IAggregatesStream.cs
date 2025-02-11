using ResultBoxes;
namespace Sekiban.Pure.OrleansEventSourcing;

public interface IAggregatesStream
{
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