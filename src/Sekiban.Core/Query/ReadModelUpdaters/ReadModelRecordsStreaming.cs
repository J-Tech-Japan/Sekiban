using System.Collections.Immutable;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelRecordsStreaming<TReadModel, TId>(ImmutableList<TReadModel> List)
    : IReadModelRecords<TReadModel, TId> where TReadModel : IReadModel<TId>;
