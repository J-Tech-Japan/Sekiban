namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelUpdated<TReadModel, TId>(TReadModel ReadModel)
    : IReadModelChange<TReadModel, TId> where TReadModel : IReadModel<TId>;
