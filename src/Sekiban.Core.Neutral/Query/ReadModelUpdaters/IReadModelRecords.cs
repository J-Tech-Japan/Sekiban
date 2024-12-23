using System.Collections.Immutable;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModelRecords<TReadModel, TId> where TReadModel : IReadModel<TId>
{
    public ImmutableList<TReadModel> List { get; }
}
