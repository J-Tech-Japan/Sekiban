using Dcb.Domain;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Tests;

public class ProjectionHeadStatusTests
{
    [Fact]
    public async Task InMemoryExecutor_ShouldReturnProjectionHeadStatus_ForRegisteredProjector()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);
        var studentId = Guid.NewGuid();

        var executionResult = await executor.ExecuteAsync(new CreateStudent(studentId, "Projection Status", 4));
        Assert.True(executionResult.IsSuccess);
        var latestCommandSortableUniqueId = executionResult.GetValue().SortableUniqueId;

        var projectionStatusResult =
            await executor.GetProjectionHeadStatusAsync<GenericTagMultiProjector<StudentProjector, StudentTag>>();

        Assert.True(projectionStatusResult.IsSuccess);
        var projectionStatus = projectionStatusResult.GetValue();
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
                latestCommandSortableUniqueId,
                StringComparison.Ordinal) >= 0);
        Assert.True(projectionStatus.Consistent.EventVersion <= projectionStatus.Current.EventVersion);
        Assert.False(projectionStatus.CatchUp.IsInProgress);
        Assert.Equal(
            projectionStatus.Current.EventVersion - projectionStatus.Consistent.EventVersion,
            projectionStatus.CatchUp.PendingStreamEventCount);
    }

    [Fact]
    public async Task InMemoryExecutor_ShouldReturnEventStoreHeadStatus_WithOptInCount()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        await executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "One", 3));
        await executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "Two", 5));

        var withoutCountResult = await executor.GetEventStoreHeadStatusAsync();
        Assert.True(withoutCountResult.IsSuccess);
        Assert.Null(withoutCountResult.GetValue().TotalEventCount);
        Assert.NotNull(withoutCountResult.GetValue().LatestSortableUniqueId);

        var withCountResult = await executor.GetEventStoreHeadStatusAsync(includeTotalEventCount: true);
        Assert.True(withCountResult.IsSuccess);
        Assert.Equal(2, withCountResult.GetValue().TotalEventCount);
        Assert.NotNull(withCountResult.GetValue().LatestSortableUniqueId);
    }
}
