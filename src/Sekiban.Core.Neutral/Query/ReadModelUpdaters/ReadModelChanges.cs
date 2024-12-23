using System.Collections.Immutable;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelChanges<TReadModel, TId>(
    ImmutableList<IReadModelChange<TReadModel, TId>> ReadModels,
    DateTime TargetTimeUtc) where TReadModel : IReadModel<TId>
{
    public static ReadModelChanges<TReadModel, TId> FromReadModels(ImmutableList<TReadModel> readModels) =>
        FromReadModels(readModels, DateTime.UtcNow);
    public static ReadModelChanges<TReadModel, TId> FromReadModels(
        ImmutableList<TReadModel> readModels,
        DateTime targetTimeUtc) =>
        new(
            readModels
                .Select(m => (IReadModelChange<TReadModel, TId>)new ReadModelUnchanged<TReadModel, TId>(m))
                .ToImmutableList(),
            targetTimeUtc);
}
