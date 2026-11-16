using Dcb.Domain.WithoutResult;
using Sekiban.Dcb.TestSupport;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests.Capabilities;

public class InMemoryExecutorStoreIsolationTests
{
    [Fact]
    public async Task BuiltInStore_KeepsItsOwnCopyOfWrittenAndReadEvents()
    {
#pragma warning disable CS0618 // the obsolete constructor is the only way to get the built-in store
        var executor = new Sekiban.Dcb.InMemory.InMemoryDcbExecutor(DomainType.GetDomainTypes());
#pragma warning restore CS0618
        await InMemoryExecutorStoreIsolation.AssertStoredHistoryIsIsolatedAsync(executor);
    }
}
