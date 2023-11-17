namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelInserted<TReadModel>(TReadModel ReadModel) : IReadModelChange<TReadModel> where TReadModel : IReadModel;