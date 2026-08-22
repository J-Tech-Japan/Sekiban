using System.Reflection;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Frozen additive SEK-G40 public-contract inventory. This inventories every new shape rather than merely asserting
///     assignability: a renamed DTO member, changed positional/deconstruction shape, altered facade signature, or
///     broadened capability descriptor must break here. Existing V1 goldens/no-migration tests remain the counter-proof
///     that the old payload and surface were not changed in place.
/// </summary>
public sealed class ExpectedTagPositionCompatibilityInventoryTests
{
    [Fact]
    public void ThreeStateRequestResultConflictAndExceptionShapes_AreFrozen()
    {
        Assert.Equal(
            new[]
            {
                TagHeadExpectationKind.Unknown,
                TagHeadExpectationKind.NoEnforcement,
                TagHeadExpectationKind.AssertEmpty,
                TagHeadExpectationKind.Exact
            },
            Enum.GetValues<TagHeadExpectationKind>());
        Assert.Equal(0, (int)TagHeadExpectationKind.Unknown);
        Assert.Equal(1, (int)TagHeadExpectationKind.NoEnforcement);
        Assert.Equal(2, (int)TagHeadExpectationKind.AssertEmpty);
        Assert.Equal(3, (int)TagHeadExpectationKind.Exact);

        AssertRecordShape<TagHeadExpectation>(
            ["Kind", "Position"],
            [typeof(TagHeadExpectationKind), typeof(string)],
            ["Kind", "Position"]);
        var expectationConstructor = Assert.Single(typeof(TagHeadExpectation).GetConstructors());
        Assert.True(expectationConstructor.GetParameters()[1].HasDefaultValue);
        Assert.Null(expectationConstructor.GetParameters()[1].DefaultValue);
        Assert.Equal(TagHeadExpectationKind.NoEnforcement, TagHeadExpectation.NoEnforcement().Kind);
        Assert.Equal(TagHeadExpectationKind.AssertEmpty, TagHeadExpectation.AssertEmpty().Kind);
        Assert.Equal((TagHeadExpectationKind.Exact, "p"),
            (TagHeadExpectation.Exact("p").Kind, TagHeadExpectation.Exact("p").Position));
        AssertStaticMethod(typeof(TagHeadExpectation), nameof(TagHeadExpectation.NoEnforcement), typeof(TagHeadExpectation));
        AssertStaticMethod(typeof(TagHeadExpectation), nameof(TagHeadExpectation.AssertEmpty), typeof(TagHeadExpectation));
        AssertStaticMethod(typeof(TagHeadExpectation), nameof(TagHeadExpectation.Exact), typeof(TagHeadExpectation), typeof(string));

        AssertRecordShape<TagHeadExpectationEntry>(
            ["ServiceId", "Tag", "Expectation"],
            [typeof(string), typeof(string), typeof(TagHeadExpectation)],
            ["ServiceId", "Tag", "Expectation"]);
        AssertRecordShape<ExpectedTagPositionSpecification>(
            ["Entries"],
            [typeof(IReadOnlyList<TagHeadExpectationEntry>)],
            ["Entries", "RequiresEnforcement"]);
        var specification = new ExpectedTagPositionSpecification(
            [new TagHeadExpectationEntry("svc", "Tag:one", TagHeadExpectation.NoEnforcement())]);
        Assert.False(specification.RequiresEnforcement);
        Assert.True(new ExpectedTagPositionSpecification(
            [new TagHeadExpectationEntry("svc", "Tag:one", TagHeadExpectation.AssertEmpty())]).RequiresEnforcement);

        AssertRecordShape<TagHeadExpectedObserved>(
            ["ServiceId", "Tag", "Expected", "ObservedPosition"],
            [typeof(string), typeof(string), typeof(TagHeadExpectation), typeof(string)],
            ["ServiceId", "Tag", "Expected", "ObservedPosition"]);
        AssertRecordShape<ExpectedTagPositionWriteResult>(
            ["Events", "TagWrites"],
            [typeof(IReadOnlyList<SerializableEvent>), typeof(IReadOnlyList<TagWriteResult>)],
            ["Events", "TagWrites"]);

        AssertConstructor(typeof(ExpectedTagPositionConflictException), [typeof(IReadOnlyList<TagHeadExpectedObserved>)]);
        AssertProperty(typeof(ExpectedTagPositionConflictException), nameof(ExpectedTagPositionConflictException.Pairs),
            typeof(IReadOnlyList<TagHeadExpectedObserved>));
        AssertConstructor(typeof(TagHeadExpectationValidationException), [typeof(string)]);
        AssertConstructor(typeof(TagHeadPositionValidationException), [typeof(string)]);
        AssertConstructor(typeof(TagHeadEnforcementNotEnabledException), [typeof(string)]);
        AssertProperty(typeof(TagHeadEnforcementNotEnabledException), nameof(TagHeadEnforcementNotEnabledException.ServiceId), typeof(string));
    }

    [Fact]
    public void CapabilityDescriptorAndOptionalStoreContract_AreFrozen()
    {
        Assert.Equal(
            new[]
            {
                WriteConditionKind.Unknown,
                WriteConditionKind.SingleEventUniqueKey,
                WriteConditionKind.ExpectedTagPosition
            },
            Enum.GetValues<WriteConditionKind>());
        Assert.Equal(0, (int)WriteConditionKind.Unknown);
        Assert.Equal(1, (int)WriteConditionKind.SingleEventUniqueKey);
        Assert.Equal(2, (int)WriteConditionKind.ExpectedTagPosition);

        AssertRecordShape<WriteConditionCapabilityDescriptor>(
            ["SupportedKinds", "ProviderName"],
            [typeof(IReadOnlySet<WriteConditionKind>), typeof(string)],
            ["SupportedKinds", "ProviderName"]);
        AssertInstanceMethod(typeof(WriteConditionCapabilityDescriptor), nameof(WriteConditionCapabilityDescriptor.Supports),
            typeof(bool), typeof(WriteConditionKind));
        AssertStaticMethod(typeof(WriteConditionCapabilityDescriptor), nameof(WriteConditionCapabilityDescriptor.None),
            typeof(WriteConditionCapabilityDescriptor), typeof(string));
        AssertStaticMethod(typeof(WriteConditionCapabilityDescriptor), nameof(WriteConditionCapabilityDescriptor.Supporting),
            typeof(WriteConditionCapabilityDescriptor), typeof(string), typeof(WriteConditionKind[]));
        AssertStaticMethod(typeof(WriteConditionCapabilityDescriptor), nameof(WriteConditionCapabilityDescriptor.Intersect),
            typeof(WriteConditionCapabilityDescriptor), typeof(string), typeof(IReadOnlyCollection<WriteConditionCapabilityDescriptor>));
        AssertInstanceMethod(typeof(WriteConditionCapabilityDescriptor), nameof(ToString), typeof(string));
        AssertInstanceMethod(typeof(IWriteConditionCapabilityProvider), nameof(IWriteConditionCapabilityProvider.DescribeWriteConditions),
            typeof(WriteConditionCapabilityDescriptor));

        AssertMethod(
            typeof(IExpectedTagPositionEventStore),
            nameof(IExpectedTagPositionEventStore.EnsureExpectedTagPositionEnforcementEnabledAsync),
            typeof(Task<ResultBox<bool>>),
            typeof(CancellationToken));
        AssertMethod(
            typeof(IExpectedTagPositionEventStore),
            nameof(IExpectedTagPositionEventStore.WriteSerializableEventsWithExpectedTagPositionsAsync),
            typeof(Task<ResultBox<ExpectedTagPositionWriteResult>>),
            typeof(IReadOnlyList<SerializableEvent>), typeof(ExpectedTagPositionSpecification), typeof(CancellationToken));
    }

    [Fact]
    public void VersionedWireAndWithResultFacadeSignatures_AreAdditiveAndExact()
    {
        AssertRecordShape<VersionedExpectedTagPositionSerializedCommitRequest>(
            ["Version", "EventCandidates", "ConsistencyTags", "ExpectedTagPositions"],
            [typeof(int), typeof(IReadOnlyList<SerializableEventCandidate>), typeof(IReadOnlyList<ConsistencyTagEntry>),
                typeof(IReadOnlyList<TagHeadExpectationEntry>)],
            ["Version", "EventCandidates", "ConsistencyTags", "ExpectedTagPositions"]);
        Assert.Equal(2, VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion);

        AssertMethod(
            typeof(ISerializedExpectedTagPositionSekibanDcbExecutor),
            nameof(ISerializedExpectedTagPositionSekibanDcbExecutor.CommitSerializableEventsWithExpectedTagPositionsAsync),
            typeof(Task<ResultBox<SerializedCommitResult>>),
            typeof(VersionedExpectedTagPositionSerializedCommitRequest), typeof(CancellationToken));
        AssertSerializedExpectedSurface(typeof(GeneralSekibanExecutor));
        AssertSerializedExpectedSurface(typeof(Sekiban.Dcb.Orleans.OrleansDcbExecutor));

        Assert.True(typeof(ISerializedExpectedTagPositionSekibanDcbExecutor).IsAssignableFrom(typeof(GeneralSekibanExecutor)));
        Assert.True(typeof(IConditionalCommandExecutor).IsAssignableFrom(typeof(GeneralSekibanExecutor)));
        AssertWithResultConditionalExecutorSignatures(typeof(IConditionalCommandExecutor));

        var optionsProperty = typeof(CommandExecutionOptions).GetProperty(nameof(CommandExecutionOptions.ExpectedTagPositions));
        Assert.NotNull(optionsProperty);
        Assert.Equal(typeof(ExpectedTagPositionSpecification), optionsProperty.PropertyType);
        Assert.True(optionsProperty.SetMethod is not null);
        Assert.Equal(
            ["ConditionalAppend", "ExpectedTagPositions"],
            typeof(CommandExecutionOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => property.Name));
        Assert.Equal(typeof(ConditionalAppendSpecification),
            typeof(CommandExecutionOptions).GetProperty(nameof(CommandExecutionOptions.ConditionalAppend))!.PropertyType);
        Assert.NotNull(typeof(CommandExecutionOptions).GetConstructor(Type.EmptyTypes));
    }

    private static void AssertWithResultConditionalExecutorSignatures(Type surface)
    {
        var methods = surface.GetMethods().Where(method => method.Name == nameof(IConditionalCommandExecutor.ExecuteAsync)).ToArray();
        Assert.Equal(2, methods.Length);

        var withHandler = Assert.Single(methods, method => method.GetParameters().Length == 4);
        Assert.Equal(typeof(Task<ResultBox<ExecutionResult>>), withHandler.ReturnType);
        Assert.True(withHandler.IsGenericMethodDefinition);
        var command = withHandler.GetGenericArguments().Single();
        var parameters = withHandler.GetParameters();
        Assert.Equal(["command", "handlerFunc", "options", "cancellationToken"], parameters.Select(parameter => parameter.Name));
        Assert.Equal(command, parameters[0].ParameterType);
        Assert.Equal(typeof(CommandExecutionOptions), parameters[2].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[3].ParameterType);
        Assert.True(parameters[3].IsOptional);
        Assert.Contains(typeof(ICommand), command.GetGenericParameterConstraints());
        var handlerParameter = parameters[1].ParameterType;
        Assert.True(handlerParameter.IsGenericType);
        Assert.Equal(typeof(Func<,,>), handlerParameter.GetGenericTypeDefinition());
        Assert.Equal(
            [command, typeof(ICommandContext), typeof(Task<ResultBox<EventOrNone>>)],
            handlerParameter.GetGenericArguments());

        var selfHandling = Assert.Single(methods, method => method.GetParameters().Length == 3);
        Assert.Equal(typeof(Task<ResultBox<ExecutionResult>>), selfHandling.ReturnType);
        Assert.True(selfHandling.IsGenericMethodDefinition);
        var selfParameters = selfHandling.GetParameters();
        var selfCommand = selfHandling.GetGenericArguments().Single();
        Assert.Equal(["command", "options", "cancellationToken"], selfParameters.Select(parameter => parameter.Name));
        Assert.Equal(selfCommand, selfParameters[0].ParameterType);
        Assert.Equal(typeof(CommandExecutionOptions), selfParameters[1].ParameterType);
        Assert.Equal(typeof(CancellationToken), selfParameters[2].ParameterType);
        Assert.True(selfParameters[2].IsOptional);
        Assert.Contains(typeof(ICommandWithHandler<>).MakeGenericType(selfCommand), selfCommand.GetGenericParameterConstraints());
    }

    private static void AssertSerializedExpectedSurface(Type facade) =>
        AssertMethod(
            facade,
            nameof(ISerializedExpectedTagPositionSekibanDcbExecutor.CommitSerializableEventsWithExpectedTagPositionsAsync),
            typeof(Task<ResultBox<SerializedCommitResult>>),
            typeof(VersionedExpectedTagPositionSerializedCommitRequest), typeof(CancellationToken));

    private static void AssertRecordShape<T>(
        IReadOnlyList<string> parameterNames,
        IReadOnlyList<Type> parameterTypes,
        IReadOnlyList<string> propertyNames)
    {
        var type = typeof(T);
        AssertConstructor(type, parameterTypes, parameterNames);
        Assert.Equal(
            propertyNames,
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => property.Name));
        var deconstruct = Assert.Single(
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name == "Deconstruct");
        Assert.Equal(parameterTypes.Count, deconstruct.GetParameters().Length);
        Assert.All(deconstruct.GetParameters(), parameter => Assert.True(parameter.IsOut));
        Assert.Equal(parameterTypes.Select(type => type.MakeByRefType()),
            deconstruct.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static void AssertConstructor(Type type, IReadOnlyList<Type> parameterTypes, IReadOnlyList<string>? parameterNames = null)
    {
        var constructor = Assert.Single(
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance),
            candidate => candidate.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes));
        if (parameterNames is not null)
        {
            Assert.Equal(parameterNames, constructor.GetParameters().Select(parameter => parameter.Name));
        }
    }

    private static void AssertProperty(Type type, string name, Type propertyType)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.Equal(propertyType, property.PropertyType);
    }

    private static void AssertMethod(Type type, string name, Type returnType, params Type[] parameterTypes)
    {
        var method = Assert.Single(
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            candidate => candidate.Name == name && candidate.ReturnType == returnType &&
                         candidate.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes));
        Assert.False(method.IsStatic);
    }

    private static void AssertInstanceMethod(Type type, string name, Type returnType, params Type[] parameterTypes) =>
        AssertMethod(type, name, returnType, parameterTypes);

    private static void AssertStaticMethod(Type type, string name, Type returnType, params Type[] parameterTypes)
    {
        var method = Assert.Single(
            type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly),
            candidate => candidate.Name == name && candidate.ReturnType == returnType &&
                         candidate.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes));
        Assert.True(method.IsStatic);
    }
}
