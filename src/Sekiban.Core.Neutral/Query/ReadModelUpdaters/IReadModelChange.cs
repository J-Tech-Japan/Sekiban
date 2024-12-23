namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModelChange<TReadModel, TId> where TReadModel : IReadModel<TId>
{
    public TReadModel ReadModel { get; }
}
