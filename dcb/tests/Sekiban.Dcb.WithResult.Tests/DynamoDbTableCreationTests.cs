using System.Collections.Concurrent;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.DynamoDB;

namespace Sekiban.Dcb.Tests;

[Trait("Category", "DynamoDbLocal")]
public sealed class DynamoDbTableCreationTests
{
    [Fact]
    public async Task ConcurrentCreators_AllCompleteAndTablesAreActive()
    {
        const int callerCount = 3;
        await using var tables = new TestTables();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var gate = new MissingTableGate(callerCount);
        var clients = Enumerable.Range(0, callerCount)
            .Select(_ => new DelegatingClient(tables.Client, tables.Options.EventsTableName, gate))
            .ToArray();
        var loggers = clients.Select(_ => new RecordingLogger()).ToArray();

        try
        {
            var contexts = clients.Select((client, index) =>
                new DynamoDbContext(client, Options.Create(tables.Options), loggers[index])).ToArray();

            await Task.WhenAll(contexts.Select(context => context.EnsureTablesAsync(timeout.Token)));

            Assert.Equal(callerCount, clients.Sum(client => client.EventsCreateCalls));
            Assert.Equal(callerCount - 1, clients.Sum(client => client.EventsCreateCollisions));
            Assert.All(clients, client => Assert.True(client.EventsDescribeCalls >= 2));
            Assert.All(loggers, logger =>
            {
                Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
                Assert.Equal(3, logger.Entries.Count(entry => entry.EventId.Id == 2));
            });
            await tables.AssertActiveAsync(timeout.Token);
        }
        finally
        {
            foreach (var client in clients)
                client.Dispose();
        }
    }

    [Fact]
    public async Task SingleCreator_CreatesAllThreeTables()
    {
        await using var tables = new TestTables();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var client = new DelegatingClient(tables.Client, tables.Options.EventsTableName);
        var context = new DynamoDbContext(client, Options.Create(tables.Options));

        await context.EnsureTablesAsync(timeout.Token);

        Assert.Equal(3, client.CreateCalls);
        await tables.AssertActiveAsync(timeout.Token);
    }

    [Fact]
    public async Task ExistingActiveTables_AreAcceptedWithoutCreateCalls()
    {
        await using var tables = new TestTables();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var creator = new DynamoDbContext(tables.Client, Options.Create(tables.Options));
        await creator.EnsureTablesAsync(timeout.Token);
        await tables.AssertActiveAsync(timeout.Token);
        using var client = new DelegatingClient(tables.Client, tables.Options.EventsTableName);
        var context = new DynamoDbContext(client, Options.Create(tables.Options));

        await context.EnsureTablesAsync(timeout.Token);

        Assert.Equal(0, client.CreateCalls);
        await tables.AssertActiveAsync(timeout.Token);
    }

    private static AmazonDynamoDBConfig LocalConfig() => new()
    {
        ServiceURL = Environment.GetEnvironmentVariable("SEKIBAN_DYNAMODB_LOCAL_ENDPOINT")
            ?? "http://127.0.0.1:18000",
        AuthenticationRegion = "us-east-1"
    };

    private sealed class TestTables : IAsyncDisposable
    {
        public AmazonDynamoDBClient Client { get; } = new(
            new BasicAWSCredentials("fakeMyKeyId", "fakeSecretAccessKey"), LocalConfig());

        public DynamoDbEventStoreOptions Options { get; }

        public TestTables()
        {
            var prefix = $"G128{Guid.NewGuid():N}";
            Options = new DynamoDbEventStoreOptions
            {
                EventsTableName = $"{prefix}Events",
                TagsTableName = $"{prefix}Tags",
                ProjectionStatesTableName = $"{prefix}Projection",
                AutoCreateTables = true
            };
        }

        private string[] TableNames =>
            [Options.EventsTableName, Options.TagsTableName, Options.ProjectionStatesTableName];

        public async Task AssertActiveAsync(CancellationToken cancellationToken)
        {
            foreach (var tableName in TableNames)
            {
                var response = await Client.DescribeTableAsync(tableName, cancellationToken);
                Assert.Equal(TableStatus.ACTIVE, response.Table.TableStatus);
            }
        }

        public async ValueTask DisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                // Attempt every deletion even if one fails, including after a failed race test.
                await Task.WhenAll(TableNames.Select(async tableName =>
                {
                    try
                    {
                        await Client.DeleteTableAsync(tableName, timeout.Token);
                    }
                    catch (ResourceNotFoundException)
                    {
                        // A failing initialization may not have reached this table.
                    }
                }));
            }
            finally
            {
                Client.Dispose();
            }
        }
    }

    private sealed class MissingTableGate(int callerCount)
    {
        private readonly TaskCompletionSource _allMissing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _missingCount;

        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _missingCount) == callerCount)
                _allMissing.TrySetResult();
            await _allMissing.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
    }

    private sealed class DelegatingClient(
        IAmazonDynamoDB inner,
        string eventsTableName,
        MissingTableGate? gate = null)
        : AmazonDynamoDBClient(new BasicAWSCredentials("fakeMyKeyId", "fakeSecretAccessKey"), LocalConfig())
    {
        public int CreateCalls;
        public int EventsCreateCalls;
        public int EventsCreateCollisions;
        public int EventsDescribeCalls;

        public override async Task<DescribeTableResponse> DescribeTableAsync(
            string tableName, CancellationToken cancellationToken = default)
        {
            var firstEventsDescribe = tableName == eventsTableName &&
                Interlocked.Increment(ref EventsDescribeCalls) == 1;
            try
            {
                return await inner.DescribeTableAsync(tableName, cancellationToken);
            }
            catch (ResourceNotFoundException) when (firstEventsDescribe && gate is not null)
            {
                // Every caller has observed the real missing table before any create can run.
                // Gate only the first describe of the first table: polling and later tables must proceed.
                await gate.ArriveAsync(cancellationToken);
                throw;
            }
        }

        public override async Task<CreateTableResponse> CreateTableAsync(
            CreateTableRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CreateCalls);
            if (request.TableName == eventsTableName)
                Interlocked.Increment(ref EventsCreateCalls);
            try
            {
                return await inner.CreateTableAsync(request, cancellationToken);
            }
            catch (ResourceInUseException) when (request.TableName == eventsTableName)
            {
                Interlocked.Increment(ref EventsCreateCollisions);
                throw;
            }
        }
    }

    private sealed class RecordingLogger : ILogger<DynamoDbContext>
    {
        public ConcurrentQueue<(LogLevel Level, EventId EventId)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, eventId));
    }
}
