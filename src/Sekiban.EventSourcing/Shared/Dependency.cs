using Sekiban.EventSourcing.Snapshots.SnapshotManager;
using Sekiban.EventSourcing.Snapshots.SnapshotManager.Commands;
using System.Reflection;
namespace Sekiban.EventSourcing.Shared;

public static class Dependency
{
    public static Assembly GetAssembly() =>
        Assembly.GetExecutingAssembly();

    public static IEnumerable<(Type serviceType, Type? implementationType)> GetDependencies()
    {
        // Aggregate: RecentInMemoryActivity
        yield return (
            typeof(ICreateAggregateCommandHandler<SnapshotManager,
                CreateSnapshotManager>),
            typeof(CreateSnapshotManagerHandler));
    }
}
