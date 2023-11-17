namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModelChange<TReadModel> where TReadModel : IReadModel
{
    public TReadModel ReadModel { get; }
}