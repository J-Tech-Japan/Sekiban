using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

/// <summary>
///     Structural counterpart to the real-table writer test: it follows each compiled async writer state machine and
///     proves that the ordinary typed, ordinary serialized, and unique conditional-claim paths all call the one private
///     canonical PostgreSQL head seam. Removing a forwarding call is therefore a test-killing mutation even if another
///     writer's integration test still happens to advance a head.
/// </summary>
public sealed class PostgresTagHeadWriterSeamArchitectureTests
{
    private const string CanonicalSeam = "WriteSerializableBatchThroughCanonicalHeadSeamAsync";

    [Fact]
    public void EveryRequiredPostgresTaggedWriter_CallsTheOneCanonicalHeadSeam()
    {
        var store = typeof(PostgresEventStore);
        var writers = new[]
        {
            store.GetMethod(nameof(PostgresEventStore.WriteEventsAsync), BindingFlags.Instance | BindingFlags.Public),
            store.GetMethod(nameof(PostgresEventStore.WriteSerializableEventsAsync), BindingFlags.Instance | BindingFlags.Public),
            store.GetMethod(nameof(PostgresEventStore.WriteSerializableEventsWithExpectedTagPositionsAsync), BindingFlags.Instance | BindingFlags.Public),
            store.GetMethod("TryWriteConditionalClaimAsync", BindingFlags.Instance | BindingFlags.NonPublic)
        };

        Assert.All(writers, writer =>
        {
            Assert.NotNull(writer);
            Assert.True(AsyncStateMachineCalls(writer!, CanonicalSeam),
                $"{writer!.Name} no longer reaches {CanonicalSeam}; a tagged PostgreSQL writer would bypass the durable head protocol.");
        });
    }

    private static bool AsyncStateMachineCalls(MethodInfo method, string calleeName)
    {
        var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new InvalidOperationException($"{method.Name} is expected to be an async writer state machine.");
        var moveNext = stateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{stateMachine.FullName} has no MoveNext method.");
        var il = moveNext.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"{stateMachine.FullName}.MoveNext has no IL body.");

        for (var index = 0; index + sizeof(int) < il.Length; index++)
        {
            if (il[index] is not (0x28 or 0x6f)) // call / callvirt
            {
                continue;
            }

            MethodBase? called;
            try
            {
                called = moveNext.Module.ResolveMethod(BitConverter.ToInt32(il, index + 1));
            }
            catch (ArgumentException)
            {
                continue;
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            if (called?.Name == calleeName && called.DeclaringType == typeof(PostgresEventStore))
            {
                return true;
            }
        }

        return false;
    }
}
