using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage.Checkpoints;
using Xunit;

namespace Sekiban.Dcb.Tests.Checkpoints;

/// <summary>
///     Checkpoint CAS writes through the real AmazonDynamoDBClient against the pinned DynamoDB Local used by CI.
///     DynamoDB Local (like the real service and LocalStack) rejects ExpressionAttributeNames / ExpressionAttributeValues
///     entries that no expression references, so every conditional write after the first one is exercised here.
/// </summary>
[Trait("Category", "DynamoDbLocal")]
public sealed class DynamoDbLocalCheckpointCasTests
{
    private const string Projector = "local-cas-p";
    private const string Version = "1.0.0";

    [Fact]
    public async Task SecondAndLaterCasWrites_Commit_AndReadBackRevisionAndEventsProcessed()
    {
        var endpoint = Environment.GetEnvironmentVariable("SEKIBAN_DYNAMODB_LOCAL_ENDPOINT")
            ?? "http://127.0.0.1:18000";
        var options = new DynamoDbEventStoreOptions
        {
            EventsTableName = $"CasEvents{Guid.NewGuid():N}",
            TagsTableName = $"CasTags{Guid.NewGuid():N}",
            ProjectionStatesTableName = $"CasProjection{Guid.NewGuid():N}",
            AutoCreateTables = true,
            UseConsistentReads = true,
            ServiceUrl = new Uri(endpoint),
            MaxRetryAttempts = 2,
            MaxRetryDelay = TimeSpan.FromMilliseconds(100)
        };
        using var client = new AmazonDynamoDBClient(
            new BasicAWSCredentials("fakeMyKeyId", "fakeSecretAccessKey"),
            new AmazonDynamoDBConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1" });
        var store = new DynamoMultiProjectionStateStore(
            new DynamoDbContext(client, Options.Create(options)),
            new FixedServiceIdProvider("local-cas"));

        try
        {
            var create = await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
            Assert.Equal(CheckpointCasStatus.Committed, create.Status);
            var slot = await ReadAsync(store);
            Assert.Equal("1", slot.Revision);

            // Second and later persists use the conditional PutItem on the exact token returned by the previous write.
            for (var eventsProcessed = 2; eventsProcessed <= 3; eventsProcessed++)
            {
                var persist = await store.ConditionalUpsertAsync(
                    Req(eventsProcessed), Payload($"p{eventsProcessed}"), CheckpointExpectation.FromSlot(slot), 1_000_000);
                Assert.True(
                    persist.Status == CheckpointCasStatus.Committed,
                    $"persist #{eventsProcessed} was {persist.Status}: {persist.Cause?.Message}");
                var next = await ReadAsync(store);
                Assert.Equal(long.Parse(slot.Revision) + 1, long.Parse(next.Revision));
                Assert.Equal(eventsProcessed, next.Record!.EventsProcessed);
                slot = next;
            }

            // Tombstone (UpdateItem) and rebuilt commit (conditional PutItem on the tombstoned token).
            var invalidate = await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(slot));
            Assert.True(
                invalidate.Status == CheckpointCasStatus.Committed,
                $"invalidate was {invalidate.Status}: {invalidate.Cause?.Message}");
            var tomb = await ReadAsync(store);
            Assert.True(tomb.IsTombstoned);

            var rebuilt = await store.CommitRebuiltAsync(Req(10), Payload("rebuilt"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
            Assert.True(
                rebuilt.Status == CheckpointCasStatus.Committed,
                $"rebuilt commit was {rebuilt.Status}: {rebuilt.Cause?.Message}");
            var final = await ReadAsync(store);
            Assert.True(final.IsActive);
            Assert.Equal(10, final.Record!.EventsProcessed);
        }
        finally
        {
            await DeleteTableAsync(client, options.ProjectionStatesTableName);
            await DeleteTableAsync(client, options.EventsTableName);
            await DeleteTableAsync(client, options.TagsTableName);
        }
    }

    private static MultiProjectionStateWriteRequest Req(long eventsProcessed) => new(
        Projector, Version, "T", "s", eventsProcessed, false, null, null, 1, 1, "w",
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "test", "h");

    private static Stream Payload(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    private static async Task<CheckpointSlot> ReadAsync(IGenerationAwareCheckpointStore store) =>
        (await store.ReadCheckpointSlotAsync(Projector, Version)).GetValue();

    private static async Task DeleteTableAsync(IAmazonDynamoDB client, string tableName)
    {
        try
        {
            await client.DeleteTableAsync(tableName);
        }
        catch (ResourceNotFoundException)
        {
            // The store may have failed before creating the table.
        }
    }
}
