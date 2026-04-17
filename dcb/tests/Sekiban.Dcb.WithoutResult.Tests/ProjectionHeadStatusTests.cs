using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.Student;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.WithoutResult.Tests;

public class ProjectionHeadStatusTests
{
    [Fact]
    public async Task InMemoryExecutor_ShouldReturnProjectionAndEventStoreHeadStatus()
    {
        var executor = (ISekibanExecutor)new InMemoryDcbExecutor(DomainType.GetDomainTypes());
        var executionResult = await executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "WithoutResult", 2));

        var projectionStatus =
            await executor.GetProjectionHeadStatusAsync<GenericTagMultiProjector<StudentProjector, StudentTag>>();

        Assert.Equal(
            GenericTagMultiProjector<StudentProjector, StudentTag>.MultiProjectorName,
            projectionStatus.ProjectorName);
        Assert.Equal(
            GenericTagMultiProjector<StudentProjector, StudentTag>.MultiProjectorVersion,
            projectionStatus.ProjectorVersion);
        Assert.True(projectionStatus.Current.EventVersion >= 1);
        Assert.NotNull(projectionStatus.Current.LastSortableUniqueId);
        Assert.True(
            string.Compare(
                projectionStatus.Current.LastSortableUniqueId,
                executionResult.SortableUniqueId,
                StringComparison.Ordinal) >= 0);
        Assert.False(projectionStatus.CatchUp.IsInProgress);

        var eventStoreHead = await executor.GetEventStoreHeadStatusAsync(includeTotalEventCount: true);
        Assert.Equal(1, eventStoreHead.TotalEventCount);
        Assert.NotNull(eventStoreHead.LatestSortableUniqueId);
    }
}
