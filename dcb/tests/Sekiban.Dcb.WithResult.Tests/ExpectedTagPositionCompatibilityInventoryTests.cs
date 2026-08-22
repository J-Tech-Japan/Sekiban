using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Pin the additive SEK-G40 public contract separately from the legacy serialized-interface freeze. This inventory
///     makes accidental discriminator/DTO/interface drift visible while the existing no-migration tests keep every V1
///     shape and <see cref="ISerializedSekibanDcbExecutor" /> member unchanged.
/// </summary>
public sealed class ExpectedTagPositionCompatibilityInventoryTests
{
    [Fact]
    public void NewCapabilityAndThreeStateContract_HaveThePinnedAdditiveShape()
    {
        Assert.Equal(2, (int)WriteConditionKind.ExpectedTagPosition);
        Assert.Equal(
            new[]
            {
                TagHeadExpectationKind.Unknown,
                TagHeadExpectationKind.NoEnforcement,
                TagHeadExpectationKind.AssertEmpty,
                TagHeadExpectationKind.Exact
            },
            Enum.GetValues<TagHeadExpectationKind>());

        Assert.Equal(TagHeadExpectationKind.NoEnforcement, TagHeadExpectation.NoEnforcement().Kind);
        Assert.Equal(TagHeadExpectationKind.AssertEmpty, TagHeadExpectation.AssertEmpty().Kind);
        Assert.Equal((TagHeadExpectationKind.Exact, "p"),
            (TagHeadExpectation.Exact("p").Kind, TagHeadExpectation.Exact("p").Position));

        Assert.Equal(
            new[] { "ServiceId", "Tag", "Expectation" },
            typeof(TagHeadExpectationEntry).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(
            new[] { "Events", "TagWrites" },
            typeof(ExpectedTagPositionWriteResult).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name));
        Assert.Equal("Pairs", typeof(ExpectedTagPositionConflictException).GetProperty(nameof(ExpectedTagPositionConflictException.Pairs))!.Name);
    }

    [Fact]
    public void VersionedWireAndOptionalInterfaces_AreAdditiveAndImplementedByTheWithResultFacade()
    {
        Assert.Equal(
            new[] { "Version", "EventCandidates", "ConsistencyTags", "ExpectedTagPositions" },
            typeof(VersionedExpectedTagPositionSerializedCommitRequest).GetConstructors().Single().GetParameters()
                .Select(parameter => parameter.Name));

        var serializedMethod = Assert.Single(typeof(ISerializedExpectedTagPositionSekibanDcbExecutor).GetMethods());
        Assert.Equal(nameof(ISerializedExpectedTagPositionSekibanDcbExecutor.CommitSerializableEventsWithExpectedTagPositionsAsync),
            serializedMethod.Name);
        Assert.Equal(
            new[] { typeof(VersionedExpectedTagPositionSerializedCommitRequest), typeof(CancellationToken) },
            serializedMethod.GetParameters().Select(parameter => parameter.ParameterType));

        var storeMethods = typeof(IExpectedTagPositionEventStore).GetMethods().Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[]
            {
                nameof(IExpectedTagPositionEventStore.EnsureExpectedTagPositionEnforcementEnabledAsync),
                nameof(IExpectedTagPositionEventStore.WriteSerializableEventsWithExpectedTagPositionsAsync)
            },
            storeMethods);
        Assert.True(typeof(ISerializedExpectedTagPositionSekibanDcbExecutor).IsAssignableFrom(typeof(GeneralSekibanExecutor)));
        Assert.True(typeof(IConditionalCommandExecutor).IsAssignableFrom(typeof(GeneralSekibanExecutor)));
    }
}
