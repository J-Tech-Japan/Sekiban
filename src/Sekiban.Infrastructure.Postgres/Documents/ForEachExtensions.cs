namespace Sekiban.Infrastructure.Postgres.Documents;

internal static class ForEachExtensions
{
    internal static void ForEach<T>(this IEnumerable<T> collection, Action<T> action)
    {
        foreach (var item in collection)
            action(item);
    }
}
