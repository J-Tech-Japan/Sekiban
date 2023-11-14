namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelDeleted<TReadModel>(TReadModel ReadModel) : IReadModelChange<TReadModel> where TReadModel : IReadModel;