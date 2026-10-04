extern alias WithoutResultFacade;
using System.Reflection;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G23 binary-compatibility regression tests: the public WithResult Orleans executor constructor that existed
///     before the IExecutedUserProvider parameter was added must still be present in the public API surface.
/// </summary>
public class ExecutorBinaryCompatibilityTests
{
    [Theory]
    [InlineData(typeof(OrleansDcbExecutor), typeof(Sekiban.Dcb.Commands.IConditionalCommandExecutor))]
    [InlineData(typeof(WithoutResultFacade::Sekiban.Dcb.Orleans.OrleansDcbExecutor),
        typeof(WithoutResultFacade::Sekiban.Dcb.Commands.IConditionalCommandExecutor))]
    public void ExecutionOptions_AreExplicitOnly(Type executor, Type capability)
    {
        Assert.True(capability.IsAssignableFrom(executor));
        var map = executor.GetInterfaceMap(capability);
        Assert.Equal(2, map.TargetMethods.Length);
        Assert.All(map.TargetMethods, method => Assert.True(method.IsPrivate));
        Assert.DoesNotContain(executor.GetMethods(BindingFlags.Instance | BindingFlags.Public),
            method => method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(Sekiban.Dcb.Commands.CommandExecutionOptions)));
    }

    // These bodies are compiled but deliberately never invoked: no cluster is needed for source compatibility.
    private static void ExistingTypedCallShapes(OrleansDcbExecutor executor, G22UpsertCommand command,
        Func<G22UpsertCommand, Sekiban.Dcb.Commands.ICommandContext,
            Task<ResultBoxes.ResultBox<Sekiban.Dcb.Events.EventOrNone>>> handler)
    {
        _ = executor.ExecuteAsync(command, handler, default);
        _ = executor.ExecuteAsync(command, default(CancellationToken));
    }

    private static void ExistingTypedCallShapes(WithoutResultFacade::Sekiban.Dcb.Orleans.OrleansDcbExecutor executor,
        PerCommandFenceFacade.WithoutCommand command,
        Func<PerCommandFenceFacade.WithoutCommand, WithoutResultFacade::Sekiban.Dcb.Commands.ICommandContext,
            Task<Sekiban.Dcb.Events.EventOrNone>> handler)
    {
        _ = executor.ExecuteAsync(command, handler, default);
        _ = executor.ExecuteAsync(command, default(CancellationToken));
    }

    [Fact]
    public void PreSekibanG23_Constructor_Overload_Is_Public()
    {
        var executorType = typeof(OrleansDcbExecutor);
        var expected = "Orleans.IClusterClient,Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher,Sekiban.Dcb.ServiceId.IServiceIdProvider".Split(',');

        var constructors = executorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        var matching = constructors.FirstOrDefault(c =>
            c.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(expected));

        Assert.NotNull(matching);
        Assert.True(matching.IsPublic);
    }

    [Fact]
    public void PreSekibanG31_LongestOrleansConstructor_IsStillPublic()
    {
        var expected = "Orleans.IClusterClient,Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher,Sekiban.Dcb.ServiceId.IServiceIdProvider,Sekiban.Dcb.IExecutedUserProvider".Split(',');
        Assert.Contains(
            typeof(OrleansDcbExecutor).GetConstructors(BindingFlags.Instance | BindingFlags.Public),
            constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType.FullName)
                .SequenceEqual(expected));
    }

    [Fact]
    public void PreSekibanG24_MultiProjectionGrain_Constructor_Is_Still_Public()
    {
        // The registry dependencies were added through an overload. Keep the exact pre-G24 surface available to
        // already-compiled Orleans activation factories and direct consumers.
        var legacy = typeof(MultiProjectionGrain)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(constructor =>
            {
                var parameters = constructor.GetParameters();
                return parameters.Length == 11 &&
                    parameters[^1].ParameterType == typeof(Sekiban.Dcb.ServiceId.IServiceIdProvider) &&
                    parameters.All(parameter => parameter.ParameterType != typeof(IProjectionStatusStore));
            });

        Assert.NotNull(legacy);
        Assert.True(legacy!.IsPublic);
    }

    [Theory]
    [InlineData(typeof(ITagConsistentGrain))]
    [InlineData(typeof(TagConsistentGrain))]
    public void MakeReservationAsync_ClrStringSignature_IsUnchanged(Type type)
    {
        var method = type.GetMethod("MakeReservationAsync", BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        Assert.Equal(typeof(string), Assert.Single(method!.GetParameters()).ParameterType);
    }
}
