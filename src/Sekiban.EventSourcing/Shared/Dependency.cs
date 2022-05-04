using Sekiban.EventSourcing.Snapshots.SnapshotManagers;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;
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
        yield return (
            typeof(IChangeAggregateCommandHandler<SnapshotManager,
                ReportAggregateVersionToSnapshotManger>),
            typeof(ReportAggregateVersionToSnapshotMangerHandler));
    }
}
