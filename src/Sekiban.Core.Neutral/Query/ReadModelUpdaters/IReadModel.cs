namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModel<TId>
{
    public TId Id { get; }
}
