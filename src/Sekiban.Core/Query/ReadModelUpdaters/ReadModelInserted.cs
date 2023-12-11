namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelInserted<TReadModel, TId>(TReadModel ReadModel) : IReadModelChange<TReadModel, TId> where TReadModel : IReadModel<TId>;
