namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelUpdated<TReadModel>(TReadModel ReadModel) : IReadModelChange<TReadModel> where TReadModel : IReadModel;