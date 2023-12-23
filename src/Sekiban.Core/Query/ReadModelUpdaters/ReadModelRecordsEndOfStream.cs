using System.Collections.Immutable;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelRecordsEndOfStream<TReadModel, TId>(ImmutableList<TReadModel> List)
    : IReadModelRecords<TReadModel, TId> where TReadModel : IReadModel<TId>;
