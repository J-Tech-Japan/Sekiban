using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sekiban.Dcb.DynamoDB;

/// <summary>
///     Context for DynamoDB table access and initialization.
/// </summary>
public class DynamoDbContext
{
    public const string EventsGsiName = "GSI1";
    public const string TagsGsiName = "GSI1";
    public const string EventsGsiPartitionKey = "ALL_EVENTS";

    private static readonly Action<ILogger, string, Exception?> LogEnsuringTable =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, nameof(LogEnsuringTable)),
            "Ensuring DynamoDB table exists: {TableName}");

    private static readonly Action<ILogger, string, Exception?> LogTableReady =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(2, nameof(LogTableReady)),
            "DynamoDB table ready: {TableName}");

    private static readonly Action<ILogger, string, Exception?> LogTableCreateFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(3, nameof(LogTableCreateFailed)),
            "Failed to create DynamoDB table: {TableName}");

    private readonly IAmazonDynamoDB _client;
    private readonly DynamoDbEventStoreOptions _options;
    private readonly ILogger<DynamoDbContext>? _logger;
    private bool _tablesEnsured;

    public DynamoDbContext(
        IAmazonDynamoDB client,
        IOptions<DynamoDbEventStoreOptions> options,
        ILogger<DynamoDbContext>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    public IAmazonDynamoDB Client => _client;

    public DynamoDbEventStoreOptions Options => _options;

    public string EventsTableName => _options.EventsTableName;

    public string TagsTableName => _options.TagsTableName;

    public string ProjectionStatesTableName => _options.ProjectionStatesTableName;

    public async Task EnsureTablesAsync(CancellationToken cancellationToken = default)
    {
        if (_tablesEnsured || !_options.AutoCreateTables)
            return;

        await EnsureTableExistsAsync(BuildEventsTableRequest(), cancellationToken).ConfigureAwait(false);
        await EnsureTableExistsAsync(BuildTagsTableRequest(), cancellationToken).ConfigureAwait(false);
        await EnsureTableExistsAsync(BuildProjectionStatesTableRequest(), cancellationToken).ConfigureAwait(false);

        _tablesEnsured = true;
    }

    private async Task EnsureTableExistsAsync(CreateTableRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (_logger != null)
            {
                LogEnsuringTable(_logger, request.TableName, null);
            }

            try
            {
                await _client.DescribeTableAsync(request.TableName, cancellationToken).ConfigureAwait(false);
                if (_logger != null)
                {
                    LogTableReady(_logger, request.TableName, null);
                }
                return;
            }
            catch (ResourceNotFoundException)
            {
                // Continue to create table
            }

            await _client.CreateTableAsync(request, cancellationToken).ConfigureAwait(false);
            await WaitForTableActiveAsync(request.TableName, cancellationToken).ConfigureAwait(false);

            if (_logger != null)
            {
                LogTableReady(_logger, request.TableName, null);
            }
        }
        catch (Exception ex)
        {
            if (_logger != null)
            {
                LogTableCreateFailed(_logger, request.TableName, ex);
            }
            throw;
        }
    }

    private async Task WaitForTableActiveAsync(string tableName, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var response = await _client.DescribeTableAsync(tableName, cancellationToken).ConfigureAwait(false);
            if (response.Table.TableStatus == TableStatus.ACTIVE)
                return;

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private CreateTableRequest BuildEventsTableRequest()
    {
        return new CreateTableRequest
        {
            TableName = EventsTableName,
            KeySchema =
            [
                new KeySchemaElement("pk", KeyType.HASH),
                new KeySchemaElement("sk", KeyType.RANGE)
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition("pk", ScalarAttributeType.S),
                new AttributeDefinition("sk", ScalarAttributeType.S),
                new AttributeDefinition("gsi1pk", ScalarAttributeType.S),
                new AttributeDefinition("sortableUniqueId", ScalarAttributeType.S)
            ],
            GlobalSecondaryIndexes =
            [
                new GlobalSecondaryIndex
                {
                    IndexName = EventsGsiName,
                    KeySchema =
                    [
                        new KeySchemaElement("gsi1pk", KeyType.HASH),
                        new KeySchemaElement("sortableUniqueId", KeyType.RANGE)
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL }
                }
            ],
            BillingMode = BillingMode.PAY_PER_REQUEST
        };
    }

    private CreateTableRequest BuildTagsTableRequest()
    {
        return new CreateTableRequest
        {
            TableName = TagsTableName,
            KeySchema =
            [
                new KeySchemaElement("pk", KeyType.HASH),
                new KeySchemaElement("sk", KeyType.RANGE)
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition("pk", ScalarAttributeType.S),
                new AttributeDefinition("sk", ScalarAttributeType.S),
                new AttributeDefinition("tagGroup", ScalarAttributeType.S),
                new AttributeDefinition("tagString", ScalarAttributeType.S)
            ],
            GlobalSecondaryIndexes =
            [
                new GlobalSecondaryIndex
                {
                    IndexName = TagsGsiName,
                    KeySchema =
                    [
                        new KeySchemaElement("tagGroup", KeyType.HASH),
                        new KeySchemaElement("tagString", KeyType.RANGE)
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL }
                }
            ],
            BillingMode = BillingMode.PAY_PER_REQUEST
        };
    }

    private CreateTableRequest BuildProjectionStatesTableRequest()
    {
        return new CreateTableRequest
        {
            TableName = ProjectionStatesTableName,
            KeySchema =
            [
                new KeySchemaElement("pk", KeyType.HASH),
                new KeySchemaElement("sk", KeyType.RANGE)
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition("pk", ScalarAttributeType.S),
                new AttributeDefinition("sk", ScalarAttributeType.S)
            ],
            BillingMode = BillingMode.PAY_PER_REQUEST
        };
    }
}
