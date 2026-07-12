using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     A <see cref="Container" /> whose every operation throws, so a test double can override just the two or
///     three the code under test is allowed to use. Anything else it reaches for fails loudly instead of
///     silently returning a default.
/// </summary>
public abstract class NotSupportedCosmosContainer : Container
{
    private static NotSupportedException Unsupported() => new("This container is a test double.");

    public override string Id => throw Unsupported();
    public override Database Database => throw Unsupported();
    public override Conflicts Conflicts => throw Unsupported();
    public override Microsoft.Azure.Cosmos.Scripts.Scripts Scripts => throw Unsupported();

    public override Task<ItemResponse<T>> CreateItemAsync<T>(
        T item,
        PartitionKey? partitionKey = null,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> CreateItemStreamAsync(
        Stream streamPayload,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override TransactionalBatch CreateTransactionalBatch(PartitionKey partitionKey) => throw Unsupported();

    public override Task<ContainerResponse> DeleteContainerAsync(
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> DeleteContainerStreamAsync(
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ItemResponse<T>> DeleteItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> DeleteItemStreamAsync(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override ChangeFeedEstimator GetChangeFeedEstimator(
        string processorName,
        Container leaseContainer) => throw Unsupported();

    public override ChangeFeedProcessorBuilder GetChangeFeedEstimatorBuilder(
        string processorName,
        ChangesEstimationHandler estimationDelegate,
        TimeSpan? estimationPeriod = null) => throw Unsupported();

    public override FeedIterator<T> GetChangeFeedIterator<T>(
        ChangeFeedStartFrom changeFeedStartFrom,
        ChangeFeedMode changeFeedMode,
        ChangeFeedRequestOptions? changeFeedRequestOptions = null) => throw Unsupported();

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder(
        string processorName,
        ChangeFeedStreamHandler onChangesDelegate) => throw Unsupported();

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(
        string processorName,
        ChangeFeedHandler<T> onChangesDelegate) => throw Unsupported();

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(
        string processorName,
        ChangesHandler<T> onChangesDelegate) => throw Unsupported();

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint(
        string processorName,
        ChangeFeedStreamHandlerWithManualCheckpoint onChangesDelegate) => throw Unsupported();

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint<T>(
        string processorName,
        ChangeFeedHandlerWithManualCheckpoint<T> onChangesDelegate) => throw Unsupported();

    public override FeedIterator GetChangeFeedStreamIterator(
        ChangeFeedStartFrom changeFeedStartFrom,
        ChangeFeedMode changeFeedMode,
        ChangeFeedRequestOptions? changeFeedRequestOptions = null) => throw Unsupported();

    public override Task<IReadOnlyList<FeedRange>> GetFeedRangesAsync(
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override IOrderedQueryable<T> GetItemLinqQueryable<T>(
        bool allowSynchronousQueryExecution = false,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null,
        CosmosLinqSerializerOptions? linqSerializerOptions = null) => throw Unsupported();

    public override FeedIterator<T> GetItemQueryIterator<T>(
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override FeedIterator<T> GetItemQueryIterator<T>(
        FeedRange feedRange,
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override FeedIterator<T> GetItemQueryIterator<T>(
        string? queryText = null,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override FeedIterator GetItemQueryStreamIterator(
        FeedRange feedRange,
        QueryDefinition queryDefinition,
        string? continuationToken,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override FeedIterator GetItemQueryStreamIterator(
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override FeedIterator GetItemQueryStreamIterator(
        string? queryText = null,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override Task<ItemResponse<T>> PatchItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        IReadOnlyList<PatchOperation> patchOperations,
        PatchItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> PatchItemStreamAsync(
        string id,
        PartitionKey partitionKey,
        IReadOnlyList<PatchOperation> patchOperations,
        PatchItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ContainerResponse> ReadContainerAsync(
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> ReadContainerStreamAsync(
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ItemResponse<T>> ReadItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> ReadItemStreamAsync(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<FeedResponse<T>> ReadManyItemsAsync<T>(
        IReadOnlyList<(string id, PartitionKey partitionKey)> items,
        ReadManyRequestOptions? readManyRequestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> ReadManyItemsStreamAsync(
        IReadOnlyList<(string id, PartitionKey partitionKey)> items,
        ReadManyRequestOptions? readManyRequestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<int?> ReadThroughputAsync(CancellationToken cancellationToken = default) =>
        throw Unsupported();

    public override Task<ThroughputResponse> ReadThroughputAsync(
        RequestOptions requestOptions,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ContainerResponse> ReplaceContainerAsync(
        ContainerProperties containerProperties,
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> ReplaceContainerStreamAsync(
        ContainerProperties containerProperties,
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ItemResponse<T>> ReplaceItemAsync<T>(
        T item,
        string id,
        PartitionKey? partitionKey = null,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> ReplaceItemStreamAsync(
        Stream streamPayload,
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ThroughputResponse> ReplaceThroughputAsync(
        int throughput,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ThroughputResponse> ReplaceThroughputAsync(
        ThroughputProperties throughputProperties,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ItemResponse<T>> UpsertItemAsync<T>(
        T item,
        PartitionKey? partitionKey = null,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> UpsertItemStreamAsync(
        Stream streamPayload,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();
}
