using Sekiban.Dcb.Common;
using Sekiban.Dcb.Queries;
using Xunit.Sdk;

namespace Sekiban.Dcb.SortableUniqueIdWait.Tests;

public sealed class WaitArchitectureNegativeTests
{
    [Fact]
    public void BypassedBoundary_IsRejectedAtTheFacadeHop()
    {
        var error = Assert.Throws<AllException>(() => WaitArchitectureAssertions.AssertAll(Route(typeof(BypassFacade))));
        Assert.Contains("must call WaitBoundary exactly once", error.Message);
    }

    [Fact]
    public void DuplicatePolicyCall_IsRejectedAtTheBoundary()
    {
        var error = Assert.Throws<AllException>(() => WaitArchitectureAssertions.AssertAll(Route(typeof(DuplicateFacade))));
        Assert.Contains("wait boundary must call the shared policy exactly once", error.Message);
    }

    private static WaitArchitectureRoute Route(Type facade) => new(
        facade.Name, facade, typeof(IQueryCommon<>), "WaitBoundary", SortableUniqueIdWaitSurface.InMemorySingle);

    private sealed class BypassFacade
    {
        private readonly SortableUniqueIdWaitPolicy _policy = new();

        public Task QueryAsync<T>(IQueryCommon<T> query) where T : notnull =>
            _policy.WaitAsync("target", SortableUniqueIdWaitSurface.InMemorySingle,
                SortableUniqueIdWaitMode.Strict, _ => Task.FromResult(true));

        private Task WaitBoundary(SortableUniqueIdWaitSurface surface) =>
            _policy.WaitAsync("target", surface, SortableUniqueIdWaitMode.Strict, _ => Task.FromResult(true));
    }

    private sealed class DuplicateFacade
    {
        private readonly SortableUniqueIdWaitPolicy _policy = new();

        public Task QueryAsync<T>(IQueryCommon<T> query) where T : notnull => WaitBoundary(SortableUniqueIdWaitSurface.InMemorySingle);

        private async Task WaitBoundary(SortableUniqueIdWaitSurface surface)
        {
            await _policy.WaitAsync("target", surface, SortableUniqueIdWaitMode.Strict, _ => Task.FromResult(true));
            await _policy.WaitAsync("target", surface, SortableUniqueIdWaitMode.Strict, _ => Task.FromResult(true));
        }
    }
}
