namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelUnchanged<TReadModel, TId>(TReadModel ReadModel)
    : IReadModelChange<TReadModel, TId> where TReadModel : IReadModel<TId>;
