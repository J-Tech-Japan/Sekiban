using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

public sealed class PostgresExpectedTagPositionLimitsTests
{
    [Fact]
    public void Postgres_UsesDefaultUnlimitedLimitsWithoutAnOverride()
    {
        var contract = typeof(IExpectedTagPositionEventStore);
        var getter = contract.GetProperty(nameof(IExpectedTagPositionEventStore.ExpectedTagPositionLimits))!.GetMethod!;
        var map = typeof(PostgresEventStore).GetInterfaceMap(contract);
        var index = Array.IndexOf(map.InterfaceMethods, getter);
        Assert.True(index >= 0);
        Assert.Equal(contract, map.TargetMethods[index].DeclaringType);
        Assert.Equal(new ExpectedTagPositionLimits(null, null),
            map.TargetMethods[index].Invoke(System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PostgresEventStore)), null));
    }
}
