namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelUnchanged<TReadModel>(TReadModel ReadModel) : IReadModelChange<TReadModel> where TReadModel : IReadModel;