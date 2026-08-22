using System.Reflection;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Xunit;

namespace Sekiban.Dcb.WithoutResult.Tests;

/// <summary>
///     The exception-based half of the frozen SEK-G40 additive compatibility inventory. It lives in this assembly so
///     the same namespace names from the WithResult facade cannot mask a signature drift in the WithoutResult package.
/// </summary>
public sealed class ExpectedTagPositionWithoutResultCompatibilityInventoryTests
{
    [Fact]
    public void WithoutResultConditionalAndSerializedSurfaces_HaveExactAdditiveSignatures()
    {
        var conditionalMethods = typeof(IConditionalCommandExecutor).GetMethods()
            .Where(method => method.Name == nameof(IConditionalCommandExecutor.ExecuteAsync)).ToArray();
        Assert.Equal(2, conditionalMethods.Length);

        var withHandler = Assert.Single(conditionalMethods, method => method.GetParameters().Length == 4);
        Assert.Equal(typeof(Task<ExecutionResult>), withHandler.ReturnType);
        Assert.True(withHandler.IsGenericMethodDefinition);
        var command = withHandler.GetGenericArguments().Single();
        var parameters = withHandler.GetParameters();
        Assert.Equal(["command", "handlerFunc", "options", "cancellationToken"], parameters.Select(parameter => parameter.Name));
        Assert.Equal(command, parameters[0].ParameterType);
        Assert.Equal(typeof(CommandExecutionOptions), parameters[2].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[3].ParameterType);
        Assert.True(parameters[3].IsOptional);
        Assert.Contains(typeof(ICommand), command.GetGenericParameterConstraints());
        Assert.Equal(typeof(Func<,,>), parameters[1].ParameterType.GetGenericTypeDefinition());
        Assert.Equal(
            [command, typeof(ICommandContext), typeof(Task<EventOrNone>)],
            parameters[1].ParameterType.GetGenericArguments());

        var selfHandling = Assert.Single(conditionalMethods, method => method.GetParameters().Length == 3);
        Assert.Equal(typeof(Task<ExecutionResult>), selfHandling.ReturnType);
        Assert.True(selfHandling.IsGenericMethodDefinition);
        var selfParameters = selfHandling.GetParameters();
        var selfCommand = selfHandling.GetGenericArguments().Single();
        Assert.Equal(["command", "options", "cancellationToken"], selfParameters.Select(parameter => parameter.Name));
        Assert.Equal(selfCommand, selfParameters[0].ParameterType);
        Assert.Equal(typeof(CommandExecutionOptions), selfParameters[1].ParameterType);
        Assert.Equal(typeof(CancellationToken), selfParameters[2].ParameterType);
        Assert.True(selfParameters[2].IsOptional);
        Assert.Contains(typeof(ICommandWithHandler<>).MakeGenericType(selfCommand), selfCommand.GetGenericParameterConstraints());

        Assert.True(typeof(IConditionalCommandExecutor).IsAssignableFrom(typeof(GeneralSekibanExecutor)));
        Assert.True(typeof(ISerializedExpectedTagPositionSekibanDcbExecutor).IsAssignableFrom(typeof(GeneralSekibanExecutor)));
        Assert.True(typeof(ISerializedExpectedTagPositionSekibanDcbExecutor)
            .IsAssignableFrom(typeof(Sekiban.Dcb.Orleans.OrleansDcbExecutor)));
        AssertSerializedSurface(typeof(GeneralSekibanExecutor));
        AssertSerializedSurface(typeof(Sekiban.Dcb.Orleans.OrleansDcbExecutor));
    }

    private static void AssertSerializedSurface(Type facade)
    {
        var method = Assert.Single(facade.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            candidate => candidate.Name == nameof(ISerializedExpectedTagPositionSekibanDcbExecutor.CommitSerializableEventsWithExpectedTagPositionsAsync) &&
                         candidate.ReturnType == typeof(Task<ResultBox<SerializedCommitResult>>) &&
                         candidate.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
                             [typeof(VersionedExpectedTagPositionSerializedCommitRequest), typeof(CancellationToken)]));
        Assert.False(method.IsStatic);
    }
}
