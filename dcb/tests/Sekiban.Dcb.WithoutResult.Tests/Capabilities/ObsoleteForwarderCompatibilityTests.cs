using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.Student;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests.Capabilities;

/// <summary>
///     Deprecating something is a promise that it still works. These are that promise, written down.
///     The old types are <c>[Obsolete]</c>, not removed: existing code compiles, existing behaviour is identical, and
///     nothing has to be migrated on our schedule. The warning is a pointer, not a deadline.
/// </summary>
public class ObsoleteForwarderCompatibilityTests
{
#pragma warning disable CS0618 // the whole point of these tests is to use the obsolete surface

    [Fact]
    public async Task TheOldExecutorStillExecutes()
    {
        var domainTypes = DomainType.GetDomainTypes();
        // ExecuteAsync is an explicit interface implementation, then as now: the old code path is used through
        // ISekibanExecutor, which is exactly how a consumer holds it.
        ISekibanExecutor oldExecutor = new Sekiban.Dcb.InMemory.InMemoryDcbExecutor(
            domainTypes,
            new Sekiban.Dcb.InMemory.InMemoryEventStore(domainTypes.EventTypes));

        var result = await oldExecutor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "Old Path Student", 3));

        Assert.Single(result.Events);
    }

    [Fact]
    public async Task TheOldOneArgumentConstructorStillWorks_PrivateVolatileStoreAndAll()
    {
        // Still obsolete. Still silent about the store it creates. Still works — which is the promise, and also the
        // reason the package boundary had to exist: nothing here misbehaves, it just cannot be seen.
        ISekibanExecutor executor = new Sekiban.Dcb.InMemory.InMemoryDcbExecutor(DomainType.GetDomainTypes());

        var result = await executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "Legacy Student", 3));

        Assert.Single(result.Events);
    }

    [Fact]
    public void TheNewExecutorIsTheOldOne_SoAnythingTypedAgainstTheOldOneKeepsWorking()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var executor = new InMemoryDcbExecutorForTesting(
            domainTypes,
            new InMemoryEventStore(domainTypes.EventTypes));

        // Source compatibility, concretely: code that holds the old type — a helper, a base class, a test fixture —
        // takes the new one without a change.
        Sekiban.Dcb.InMemory.InMemoryDcbExecutor asOld = executor;
        Assert.NotNull(asOld);
    }

    [Fact]
    public void TheNewStoreIsTheOldStore()
    {
        var store = new InMemoryEventStore(DomainType.GetDomainTypes().EventTypes);

        Sekiban.Dcb.InMemory.InMemoryEventStore asOld = store;

        // And it still answers the durability question the same way, so SEK-G10's guard is unaffected by the move.
        Assert.Equal(StorageDurability.Volatile, asOld.DescribeStorage().Durability);
    }

    [Fact]
    public void TheTestingExecutorStillDeclaresItselfTestingInProcess()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var executor = new InMemoryDcbExecutorForTesting(
            domainTypes,
            new InMemoryEventStore(domainTypes.EventTypes));

        var runtime = ((IExecutorRuntimeDescriptorProvider)executor).DescribeRuntime();

        // The package boundary stops you composing it by accident; the descriptor stops it running if you did anyway.
        // Belt and braces, deliberately: the incident got past one layer already.
        Assert.Equal(ExecutorRuntimeKind.TestingInProcess, runtime.Runtime);
    }

    [Fact]
    public void TheProductionInternalCacheWasNotTouched()
    {
        // InMemoryTagStatePersistent is named like a test double and is not one: it is the tag-state actor's real
        // in-process cache. It stays in Sekiban.Dcb.Core and it is NOT obsolete. If a future cleanup sweeps it up with
        // the rest of the InMemory namespace, this fails.
        var type = typeof(Sekiban.Dcb.InMemory.InMemoryTagStatePersistent);

        Assert.Empty(type.GetCustomAttributes(typeof(ObsoleteAttribute), false));
        Assert.Equal("Sekiban.Dcb.Core", type.Assembly.GetName().Name);
    }

#pragma warning restore CS0618
}
