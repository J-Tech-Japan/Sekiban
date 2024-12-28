namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelDeleted<TReadModel, TId>(TReadModel ReadModel)
    : IReadModelChange<TReadModel, TId> where TReadModel : IReadModel<TId>;
