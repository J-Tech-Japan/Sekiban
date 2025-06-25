using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Cached implementation of Dapr serialization service for improved performance
/// </summary>
public class CachedDaprSerializationService : IDaprSerializationService
{
    private readonly IDaprSerializationService _innerService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachedDaprSerializationService> _logger;
    private readonly TimeSpan _cacheExpiration;

    public CachedDaprSerializationService(
        IDaprSerializationService innerService,
        IMemoryCache cache,
        ILogger<CachedDaprSerializationService> logger)
    {
        _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheExpiration = TimeSpan.FromMinutes(5);
    }

    public ValueTask<byte[]> SerializeAsync<T>(T value)
    {
        // Don't cache serialization as values may change
        return _innerService.SerializeAsync(value);
    }

    public async ValueTask<T?> DeserializeAsync<T>(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return default;
        }

        var cacheKey = GenerateCacheKey(typeof(T).Name, data);
        
        if (_cache.TryGetValue<T>(cacheKey, out var cached))
        {
            _logger.LogDebug("Cache hit for type {Type}", typeof(T).Name);
            return cached;
        }

        var result = await _innerService.DeserializeAsync<T>(data);
        
        if (result != null)
        {
            _cache.Set(cacheKey, result, _cacheExpiration);
        }

        return result;
    }

    public ValueTask<DaprAggregateSurrogate> SerializeAggregateAsync(IAggregate aggregate)
    {
        // Don't cache serialization as aggregates change frequently
        return _innerService.SerializeAggregateAsync(aggregate);
    }

    public async ValueTask<IAggregate?> DeserializeAggregateAsync(DaprAggregateSurrogate surrogate)
    {
        if (surrogate?.CompressedPayload == null || surrogate.CompressedPayload.Length == 0)
        {
            return null;
        }

        var cacheKey = $"aggregate:{surrogate.AggregateId}:{surrogate.Version}:{GenerateHash(surrogate.CompressedPayload)}";
        
        if (_cache.TryGetValue<IAggregate>(cacheKey, out var cached))
        {
            _logger.LogDebug("Cache hit for aggregate {AggregateId} version {Version}", 
                surrogate.AggregateId, surrogate.Version);
            return cached;
        }

        var result = await _innerService.DeserializeAggregateAsync(surrogate);
        
        if (result != null)
        {
            _cache.Set(cacheKey, result, _cacheExpiration);
        }

        return result;
    }

    public ValueTask<DaprCommandEnvelope> SerializeCommandAsync(ICommandWithHandlerSerializable command)
    {
        // Don't cache command serialization
        return _innerService.SerializeCommandAsync(command);
    }

    public async ValueTask<ICommandWithHandlerSerializable?> DeserializeCommandAsync(DaprCommandEnvelope envelope)
    {
        if (envelope?.CommandData == null || envelope.CommandData.Length == 0)
        {
            return null;
        }

        var cacheKey = $"command:{envelope.CommandType}:{GenerateHash(envelope.CommandData)}";
        
        if (_cache.TryGetValue<ICommandWithHandlerSerializable>(cacheKey, out var cached))
        {
            _logger.LogDebug("Cache hit for command type {CommandType}", envelope.CommandType);
            return cached;
        }

        var result = await _innerService.DeserializeCommandAsync(envelope);
        
        if (result != null)
        {
            _cache.Set(cacheKey, result, _cacheExpiration);
        }

        return result;
    }

    public ValueTask<DaprEventEnvelope> SerializeEventAsync(IEvent @event, Guid aggregateId, int version, string rootPartitionKey)
    {
        // Don't cache event serialization
        return _innerService.SerializeEventAsync(@event, aggregateId, version, rootPartitionKey);
    }

    public async ValueTask<IEvent?> DeserializeEventAsync(DaprEventEnvelope envelope)
    {
        if (envelope?.EventData == null || envelope.EventData.Length == 0)
        {
            return null;
        }

        var cacheKey = $"event:{envelope.EventType}:{envelope.EventId}";
        
        if (_cache.TryGetValue<IEvent>(cacheKey, out var cached))
        {
            _logger.LogDebug("Cache hit for event type {EventType}", envelope.EventType);
            return cached;
        }

        var result = await _innerService.DeserializeEventAsync(envelope);
        
        if (result != null)
        {
            _cache.Set(cacheKey, result, _cacheExpiration);
        }

        return result;
    }

    private static string GenerateCacheKey(string prefix, byte[] data)
    {
        var hash = GenerateHash(data);
        return $"{prefix}:{hash}";
    }

    private static string GenerateHash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(data, 0, Math.Min(data.Length, 1024)); // Hash first 1KB only
        return Convert.ToBase64String(hashBytes, 0, 8); // Use first 8 bytes of hash
    }
}