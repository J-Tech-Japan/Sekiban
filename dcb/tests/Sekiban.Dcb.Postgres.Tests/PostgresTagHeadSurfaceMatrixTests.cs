extern alias WithoutResultFacade;

using System.Text.Json;
using Dcb.Domain.Student;
using Microsoft.EntityFrameworkCore;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Postgres.DbModels;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.Tags;
using Xunit;
using WithoutResultExecutor = WithoutResultFacade::Sekiban.Dcb.Actors.GeneralSekibanExecutor;
using WithoutResultCommandContext = WithoutResultFacade::Sekiban.Dcb.Commands.ICommandContext;

namespace Sekiban.Dcb.Postgres.Tests;

/// <summary>
///     AC4's real PostgreSQL facade matrix. Every branch uses the production command or serialized facade and then a
///     fresh DbContext: the request, returned payload, dcb_events/dcb_tags mutation counts, and durable dcb_tag_heads
///     row must all agree. This intentionally makes dropping an expectation at a facade, bypassing the NoEnforcement
///     head path, or drifting a DTO/count observable.
/// </summary>
public sealed class PostgresTagHeadSurfaceMatrixTests : PostgresTestBase
{
    public PostgresTagHeadSurfaceMatrixTests(PostgresTestFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ThreeStateMatrix_PreservesRequestResultPayloadCountsAndDurableHeadsAcrossProductionSurfaces()
    {
        // The public executor constructors resolve the production default service id. Keep the store and every request
        // on that same service while tags distinguish each matrix cell.
        const string serviceId = DefaultServiceIdProvider.DefaultServiceId;
        await AssertWithResultCommandMatrixAsync(serviceId);
        await AssertWithoutResultCommandMatrixAsync(serviceId);
        await AssertLegacySerializedOmissionMatrixAsync(serviceId);
        await AssertVersionedSerializedV2MatrixAsync(serviceId);
    }

    private async Task AssertWithResultCommandMatrixAsync(string serviceId)
    {
        var store = Store(serviceId);
        var executor = new GeneralSekibanExecutor(store, new InMemoryObjectAccessor(store, Fixture.DomainTypes), Fixture.DomainTypes);
        var noEnforcement = await ExecuteWithResultAsync(executor, serviceId, "with-no-enforcement", TagHeadExpectation.NoEnforcement());

        Assert.True(noEnforcement.Result.IsSuccess, noEnforcement.Result.IsSuccess ? "" : noEnforcement.Result.GetException().ToString());
        AssertCommandRequest(noEnforcement.Request, serviceId, noEnforcement.Tag, TagHeadExpectationKind.NoEnforcement, null);
        AssertExecutionPayload(noEnforcement.Result.GetValue(), noEnforcement.StudentId, noEnforcement.Name, noEnforcement.Tag);
        await AssertTransitionAsync(serviceId, noEnforcement.Before, noEnforcement.Tag, noEnforcement.Result.GetValue().SortableUniqueId!, 1);

        await EnableEpochAsync(serviceId);
        var assertEmpty = await ExecuteWithResultAsync(executor, serviceId, "with-assert-empty", TagHeadExpectation.AssertEmpty());
        Assert.True(assertEmpty.Result.IsSuccess, assertEmpty.Result.IsSuccess ? "" : assertEmpty.Result.GetException().ToString());
        AssertCommandRequest(assertEmpty.Request, serviceId, assertEmpty.Tag, TagHeadExpectationKind.AssertEmpty, null);
        AssertExecutionPayload(assertEmpty.Result.GetValue(), assertEmpty.StudentId, assertEmpty.Name, assertEmpty.Tag);
        await AssertTransitionAsync(serviceId, assertEmpty.Before, assertEmpty.Tag, assertEmpty.Result.GetValue().SortableUniqueId!, 1);

        var exact = await ExecuteWithResultAsync(
            executor,
            serviceId,
            "with-exact",
            TagHeadExpectation.Exact(assertEmpty.Result.GetValue().SortableUniqueId!),
            assertEmpty.StudentId,
            assertEmpty.Tag);
        Assert.True(exact.Result.IsSuccess, exact.Result.IsSuccess ? "" : exact.Result.GetException().ToString());
        AssertCommandRequest(exact.Request, serviceId, exact.Tag, TagHeadExpectationKind.Exact,
            assertEmpty.Result.GetValue().SortableUniqueId);
        AssertExecutionPayload(exact.Result.GetValue(), exact.StudentId, exact.Name, exact.Tag);
        await AssertTransitionAsync(serviceId, exact.Before, exact.Tag, exact.Result.GetValue().SortableUniqueId!, 0);
    }

    private async Task AssertWithoutResultCommandMatrixAsync(string serviceId)
    {
        var store = Store(serviceId);
        var executor = new WithoutResultExecutor(
            store,
            new InMemoryObjectAccessor(store, Fixture.DomainTypes),
            Fixture.DomainTypes);
        var noEnforcement = await ExecuteWithoutResultAsync(executor, serviceId, "without-no-enforcement", TagHeadExpectation.NoEnforcement());

        AssertCommandRequest(noEnforcement.Request, serviceId, noEnforcement.Tag, TagHeadExpectationKind.NoEnforcement, null);
        AssertExecutionPayload(noEnforcement.Result, noEnforcement.StudentId, noEnforcement.Name, noEnforcement.Tag);
        await AssertTransitionAsync(serviceId, noEnforcement.Before, noEnforcement.Tag, noEnforcement.Result.SortableUniqueId!, 1);

        await EnableEpochAsync(serviceId);
        var assertEmpty = await ExecuteWithoutResultAsync(executor, serviceId, "without-assert-empty", TagHeadExpectation.AssertEmpty());
        AssertCommandRequest(assertEmpty.Request, serviceId, assertEmpty.Tag, TagHeadExpectationKind.AssertEmpty, null);
        AssertExecutionPayload(assertEmpty.Result, assertEmpty.StudentId, assertEmpty.Name, assertEmpty.Tag);
        await AssertTransitionAsync(serviceId, assertEmpty.Before, assertEmpty.Tag, assertEmpty.Result.SortableUniqueId!, 1);

        var exact = await ExecuteWithoutResultAsync(
            executor,
            serviceId,
            "without-exact",
            TagHeadExpectation.Exact(assertEmpty.Result.SortableUniqueId!),
            assertEmpty.StudentId,
            assertEmpty.Tag);
        AssertCommandRequest(exact.Request, serviceId, exact.Tag, TagHeadExpectationKind.Exact, assertEmpty.Result.SortableUniqueId);
        AssertExecutionPayload(exact.Result, exact.StudentId, exact.Name, exact.Tag);
        await AssertTransitionAsync(serviceId, exact.Before, exact.Tag, exact.Result.SortableUniqueId!, 0);
    }

    private async Task AssertLegacySerializedOmissionMatrixAsync(string serviceId)
    {
        var store = Store(serviceId);
        var executor = new GeneralSekibanExecutor(store, new InMemoryObjectAccessor(store, Fixture.DomainTypes), Fixture.DomainTypes);
        var studentId = Guid.CreateVersion7();
        var tag = new StudentTag(studentId).GetTag();
        const string name = "legacy-omission";
        var candidate = Candidate(studentId, name, tag);
        var request = new SerializedCommitRequest([candidate], [new ConsistencyTagEntry(tag, string.Empty)]);
        var before = await SnapshotAsync(serviceId);

        // There is deliberately no expectedTagPositions member on this legacy DTO: its omission is the frozen V1 route.
        AssertLegacyRequest(request, candidate, tag);
        var wire = SerializedCommitWireContract.SerializeToUtf8Bytes(request);
        AssertLegacyWire(wire);
        var result = await new SerializedCommitAcceptor(executor).AcceptAsync(wire);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        AssertSerializedPayload(result.GetValue(), candidate, tag);
        await AssertTransitionAsync(serviceId, before, tag, Assert.Single(result.GetValue().WrittenEvents).SortableUniqueIdValue, 1);
    }

    private async Task AssertVersionedSerializedV2MatrixAsync(string serviceId)
    {
        var store = Store(serviceId);
        var executor = new GeneralSekibanExecutor(store, new InMemoryObjectAccessor(store, Fixture.DomainTypes), Fixture.DomainTypes);
        var noEnforcement = await ExecuteV2Async(executor, serviceId, "v2-no-enforcement", TagHeadExpectation.NoEnforcement());

        AssertV2Request(noEnforcement.Request, noEnforcement.Candidate, serviceId, noEnforcement.Tag,
            TagHeadExpectationKind.NoEnforcement, null);
        AssertV2Wire(noEnforcement.Wire, TagHeadExpectationKind.NoEnforcement, null);
        Assert.True(noEnforcement.Result.IsSuccess, noEnforcement.Result.IsSuccess ? "" : noEnforcement.Result.GetException().ToString());
        AssertSerializedPayload(noEnforcement.Result.GetValue(), noEnforcement.Candidate, noEnforcement.Tag);
        await AssertTransitionAsync(serviceId, noEnforcement.Before, noEnforcement.Tag,
            Assert.Single(noEnforcement.Result.GetValue().WrittenEvents).SortableUniqueIdValue, 1);

        await EnableEpochAsync(serviceId);
        var assertEmpty = await ExecuteV2Async(executor, serviceId, "v2-assert-empty", TagHeadExpectation.AssertEmpty());
        AssertV2Request(assertEmpty.Request, assertEmpty.Candidate, serviceId, assertEmpty.Tag,
            TagHeadExpectationKind.AssertEmpty, null);
        AssertV2Wire(assertEmpty.Wire, TagHeadExpectationKind.AssertEmpty, null);
        Assert.True(assertEmpty.Result.IsSuccess, assertEmpty.Result.IsSuccess ? "" : assertEmpty.Result.GetException().ToString());
        AssertSerializedPayload(assertEmpty.Result.GetValue(), assertEmpty.Candidate, assertEmpty.Tag);
        await AssertTransitionAsync(serviceId, assertEmpty.Before, assertEmpty.Tag,
            Assert.Single(assertEmpty.Result.GetValue().WrittenEvents).SortableUniqueIdValue, 1);

        var exact = await ExecuteV2Async(
            executor,
            serviceId,
            "v2-exact",
            TagHeadExpectation.Exact(Assert.Single(assertEmpty.Result.GetValue().WrittenEvents).SortableUniqueIdValue),
            assertEmpty.StudentId,
            assertEmpty.Tag);
        AssertV2Request(exact.Request, exact.Candidate, serviceId, exact.Tag, TagHeadExpectationKind.Exact,
            Assert.Single(assertEmpty.Result.GetValue().WrittenEvents).SortableUniqueIdValue);
        AssertV2Wire(exact.Wire, TagHeadExpectationKind.Exact,
            Assert.Single(assertEmpty.Result.GetValue().WrittenEvents).SortableUniqueIdValue);
        Assert.True(exact.Result.IsSuccess, exact.Result.IsSuccess ? "" : exact.Result.GetException().ToString());
        AssertSerializedPayload(exact.Result.GetValue(), exact.Candidate, exact.Tag);
        await AssertTransitionAsync(serviceId, exact.Before, exact.Tag,
            Assert.Single(exact.Result.GetValue().WrittenEvents).SortableUniqueIdValue, 0);
    }

    private async Task<WithResultStep> ExecuteWithResultAsync(
        GeneralSekibanExecutor executor,
        string serviceId,
        string name,
        TagHeadExpectation expected,
        Guid? studentId = null,
        string? tag = null)
    {
        var id = studentId ?? Guid.CreateVersion7();
        var resolvedTag = tag ?? new StudentTag(id).GetTag();
        var request = Options(serviceId, resolvedTag, expected);
        var before = await SnapshotAsync(serviceId);
        var result = await executor.ExecuteAsync(
            new SurfaceMatrixCommand(),
            (_, context) => context.AppendEvent(new StudentCreated(id, name), new StudentTag(id)),
            request);
        return new WithResultStep(result, request, before, id, resolvedTag, name);
    }

    private async Task<WithoutResultStep> ExecuteWithoutResultAsync(
        WithoutResultExecutor executor,
        string serviceId,
        string name,
        TagHeadExpectation expected,
        Guid? studentId = null,
        string? tag = null)
    {
        var id = studentId ?? Guid.CreateVersion7();
        var resolvedTag = tag ?? new StudentTag(id).GetTag();
        var request = Options(serviceId, resolvedTag, expected);
        var before = await SnapshotAsync(serviceId);
        var result = await executor.ExecuteAsync(
            new SurfaceMatrixCommand(),
            (SurfaceMatrixCommand _, WithoutResultCommandContext context) =>
                context.AppendEvent(new StudentCreated(id, name), new StudentTag(id)),
            request);
        return new WithoutResultStep(result, request, before, id, resolvedTag, name);
    }

    private async Task<V2Step> ExecuteV2Async(
        GeneralSekibanExecutor executor,
        string serviceId,
        string name,
        TagHeadExpectation expected,
        Guid? studentId = null,
        string? tag = null)
    {
        var id = studentId ?? Guid.CreateVersion7();
        var resolvedTag = tag ?? new StudentTag(id).GetTag();
        var candidate = Candidate(id, name, resolvedTag);
        var request = new VersionedExpectedTagPositionSerializedCommitRequest(
            VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion,
            [candidate],
            [new ConsistencyTagEntry(resolvedTag, expected.Position ?? string.Empty)],
            [new TagHeadExpectationEntry(serviceId, resolvedTag, expected)]);
        var before = await SnapshotAsync(serviceId);
        var wire = SerializedCommitWireContract.SerializeToUtf8Bytes(request);
        var result = await new SerializedCommitAcceptor(executor).AcceptAsync(wire);
        return new V2Step(result, request, candidate, wire, before, id, resolvedTag);
    }

    private PostgresEventStore Store(string serviceId) =>
        new(Fixture.DbContextFactory, Fixture.DomainTypes.EventTypes, new FixedServiceIdProvider(serviceId));

    private async Task EnableEpochAsync(string serviceId)
    {
        await using var context = await Fixture.GetDbContextAsync();
        if (await context.TagHeadEnablementEpochs.AnyAsync(row => row.ServiceId == serviceId))
        {
            return;
        }
        context.TagHeadEnablementEpochs.Add(new DbTagHeadEnablementEpoch { ServiceId = serviceId, EnabledAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }

    private async Task<DurableCounts> SnapshotAsync(string serviceId)
    {
        await using var context = await Fixture.GetDbContextAsync();
        return new DurableCounts(
            await context.Events.AsNoTracking().CountAsync(row => row.ServiceId == serviceId),
            await context.Tags.AsNoTracking().CountAsync(row => row.ServiceId == serviceId),
            await context.TagHeads.AsNoTracking().CountAsync(row => row.ServiceId == serviceId));
    }

    private async Task AssertTransitionAsync(
        string serviceId,
        DurableCounts before,
        string tag,
        string expectedHead,
        int expectedHeadDelta)
    {
        var after = await SnapshotAsync(serviceId);
        Assert.Equal(before.Events + 1, after.Events);
        Assert.Equal(before.Tags + 1, after.Tags);
        Assert.Equal(before.Heads + expectedHeadDelta, after.Heads);
        await using var context = await Fixture.GetDbContextAsync();
        var durable = await context.TagHeads.AsNoTracking()
            .SingleAsync(row => row.ServiceId == serviceId && row.Tag == tag);
        Assert.Equal(expectedHead, durable.HeadPosition);
    }

    private static CommandExecutionOptions Options(string serviceId, string tag, TagHeadExpectation expected) =>
        new()
        {
            ExpectedTagPositions = new ExpectedTagPositionSpecification(
                [new TagHeadExpectationEntry(serviceId, tag, expected)])
        };

    private SerializableEventCandidate Candidate(Guid studentId, string name, string tag) =>
        new(
            System.Text.Encoding.UTF8.GetBytes(Fixture.DomainTypes.EventTypes.SerializeEventPayload(new StudentCreated(studentId, name))),
            nameof(StudentCreated),
            [tag]);

    private static void AssertCommandRequest(
        CommandExecutionOptions request,
        string serviceId,
        string tag,
        TagHeadExpectationKind kind,
        string? position)
    {
        var specification = Assert.IsType<ExpectedTagPositionSpecification>(request.ExpectedTagPositions);
        var entry = Assert.Single(specification.Entries);
        Assert.Equal(serviceId, entry.ServiceId);
        Assert.Equal(tag, entry.Tag);
        Assert.Equal(kind, entry.Expectation.Kind);
        Assert.Equal(position, entry.Expectation.Position);
    }

    private static void AssertExecutionPayload(ExecutionResult result, Guid studentId, string name, string tag)
    {
        Assert.NotEqual(Guid.Empty, result.EventId);
        Assert.False(string.IsNullOrWhiteSpace(result.SortableUniqueId));
        var written = Assert.Single(result.Events);
        Assert.Equal(result.SortableUniqueId, written.SortableUniqueIdValue);
        Assert.Equal(tag, Assert.Single(written.Tags));
        Assert.Equal(new StudentCreated(studentId, name), Assert.IsType<StudentCreated>(written.Payload));
        var tagWrite = Assert.Single(result.TagWrites);
        Assert.Equal(tag, tagWrite.Tag);
        Assert.True(tagWrite.Version > 0);
    }

    private static void AssertLegacyRequest(SerializedCommitRequest request, SerializableEventCandidate candidate, string tag)
    {
        Assert.Equal([candidate], request.EventCandidates);
        Assert.Equal([new ConsistencyTagEntry(tag, string.Empty)], request.ConsistencyTags);
    }

    private static void AssertLegacyWire(byte[] wire)
    {
        using var document = JsonDocument.Parse(wire);
        var root = document.RootElement;
        Assert.False(root.TryGetProperty("version", out _));
        Assert.False(root.TryGetProperty("expectedTagPositions", out _));
        Assert.Equal(1, root.GetProperty("eventCandidates").GetArrayLength());
        Assert.Equal(1, root.GetProperty("consistencyTags").GetArrayLength());
    }

    private static void AssertV2Request(
        VersionedExpectedTagPositionSerializedCommitRequest request,
        SerializableEventCandidate candidate,
        string serviceId,
        string tag,
        TagHeadExpectationKind kind,
        string? position)
    {
        Assert.Equal(VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion, request.Version);
        Assert.Equal([candidate], request.EventCandidates);
        Assert.Equal([new ConsistencyTagEntry(tag, position ?? string.Empty)], request.ConsistencyTags);
        var entry = Assert.Single(request.ExpectedTagPositions);
        Assert.Equal((serviceId, tag, kind, position),
            (entry.ServiceId, entry.Tag, entry.Expectation.Kind, entry.Expectation.Position));
    }

    private static void AssertV2Wire(byte[] wire, TagHeadExpectationKind kind, string? position)
    {
        using var document = JsonDocument.Parse(wire);
        var root = document.RootElement;
        Assert.Equal(VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion, root.GetProperty("version").GetInt32());
        Assert.Equal(1, root.GetProperty("eventCandidates").GetArrayLength());
        Assert.Equal(1, root.GetProperty("consistencyTags").GetArrayLength());
        var expectation = root.GetProperty("expectedTagPositions")[0].GetProperty("expectation");
        Assert.Equal((int)kind, expectation.GetProperty("kind").GetInt32());
        if (position is null)
        {
            Assert.Equal(JsonValueKind.Null, expectation.GetProperty("position").ValueKind);
        }
        else
        {
            Assert.Equal(position, expectation.GetProperty("position").GetString());
        }
    }

    private static void AssertSerializedPayload(SerializedCommitResult result, SerializableEventCandidate candidate, string tag)
    {
        var written = Assert.Single(result.WrittenEvents);
        Assert.Equal(candidate.Payload, written.Payload);
        Assert.Equal(candidate.EventPayloadName, written.EventPayloadName);
        Assert.Equal(candidate.Tags, written.Tags);
        Assert.NotEqual(Guid.Empty, written.Id);
        Assert.False(string.IsNullOrWhiteSpace(written.SortableUniqueIdValue));
        var tagWrite = Assert.Single(result.TagWriteResults);
        Assert.Equal(tag, tagWrite.Tag);
        Assert.True(tagWrite.Version > 0);
    }

    private sealed record SurfaceMatrixCommand : ICommand;
    private sealed record DurableCounts(int Events, int Tags, int Heads);
    private sealed record WithResultStep(
        ResultBox<ExecutionResult> Result,
        CommandExecutionOptions Request,
        DurableCounts Before,
        Guid StudentId,
        string Tag,
        string Name);
    private sealed record WithoutResultStep(
        ExecutionResult Result,
        CommandExecutionOptions Request,
        DurableCounts Before,
        Guid StudentId,
        string Tag,
        string Name);
    private sealed record V2Step(
        ResultBox<SerializedCommitResult> Result,
        VersionedExpectedTagPositionSerializedCommitRequest Request,
        SerializableEventCandidate Candidate,
        byte[] Wire,
        DurableCounts Before,
        Guid StudentId,
        string Tag);
}
