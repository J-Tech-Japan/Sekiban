using System.Collections.Immutable;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public record UnsafeReadModels<TReadModel, TId>(ImmutableList<TReadModel> ReadModels, string ServerId, Guid ReadModelInstanceId)
    where TReadModel : IReadModel<TId>;