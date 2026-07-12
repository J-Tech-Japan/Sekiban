using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;

namespace Sekiban.Dcb.Tests.Cosmos;

/// <summary>
///     A <see cref="Database" /> whose every operation throws, so a test double overrides only the two the
///     code under test may use. Anything else it reaches for fails loudly rather than silently returning a
///     default.
/// </summary>
public abstract class NotSupportedCosmosDatabase : Database
{
    private static NotSupportedException Unsupported() => new("This database is a test double.");

    public override CosmosClient Client => throw Unsupported();

    public override Task<ClientEncryptionKeyResponse> CreateClientEncryptionKeyAsync(
        ClientEncryptionKeyProperties clientEncryptionKeyProperties,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ContainerResponse> CreateContainerAsync(
        ContainerProperties containerProperties,
        int? throughput = null,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ContainerResponse> CreateContainerAsync(
        ContainerProperties containerProperties,
        ThroughputProperties throughputProperties,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ContainerResponse> CreateContainerAsync(
        string id,
        string partitionKeyPath,
        int? throughput = null,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ContainerResponse> CreateContainerIfNotExistsAsync(
        string id,
        string partitionKeyPath,
        int? throughput = null,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> CreateContainerStreamAsync(
        ContainerProperties containerProperties,
        int? throughput = null,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> CreateContainerStreamAsync(
        ContainerProperties containerProperties,
        ThroughputProperties throughputProperties,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<UserResponse> CreateUserAsync(
        string id,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override ContainerBuilder DefineContainer(string name, string partitionKeyPath) => throw Unsupported();

    public override Task<DatabaseResponse> DeleteAsync(
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> DeleteStreamAsync(
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override ClientEncryptionKey GetClientEncryptionKey(string id) => throw Unsupported();

    public override FeedIterator<ClientEncryptionKeyProperties> GetClientEncryptionKeyQueryIterator(
        QueryDefinition? queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override FeedIterator<T> GetContainerQueryIterator<T>(
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override FeedIterator<T> GetContainerQueryIterator<T>(
        string? queryText = null,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override FeedIterator GetContainerQueryStreamIterator(
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override FeedIterator GetContainerQueryStreamIterator(
        string? queryText = null,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override User GetUser(string id) => throw Unsupported();

    public override FeedIterator<T> GetUserQueryIterator<T>(
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override FeedIterator<T> GetUserQueryIterator<T>(
        string? queryText = null,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null) => throw Unsupported();

    public override Task<DatabaseResponse> ReadAsync(
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ResponseMessage> ReadStreamAsync(
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<int?> ReadThroughputAsync(CancellationToken cancellationToken = default) =>
        throw Unsupported();

    public override Task<ThroughputResponse> ReadThroughputAsync(
        RequestOptions requestOptions,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ThroughputResponse> ReplaceThroughputAsync(
        int throughput,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<ThroughputResponse> ReplaceThroughputAsync(
        ThroughputProperties throughputProperties,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public override Task<UserResponse> UpsertUserAsync(
        string id,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) => throw Unsupported();
}
