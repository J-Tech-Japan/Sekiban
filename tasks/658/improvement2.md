# Dapr Serialization Redesign

## 概要
Sekiban.Pure.Daprのシリアライゼーション機構を、Orleansバージョンと同等の性能と機能を持つように再設計する。

## 現状の問題点

### 1. パフォーマンスの問題
- JSON文字列として保存（圧縮なし）
- 型解決が実行時に行われる
- キャッシュ機構の欠如

### 2. 型安全性の欠如
- AssemblyQualifiedNameを文字列で保持
- コンパイル時の型チェックなし
- 型解決エラーのハンドリング不足

### 3. Orleansとの不整合
- Orleansは`[GenerateSerializer]`を使用
- Daprは手動JSONシリアライゼーション
- 異なるストレージ形式

## 改善設計

### 1. シリアライゼーション基盤

#### 1.1 Surrogateパターンの導入

```csharp
// サロゲート基底クラス
public abstract class DaprSurrogate<T>
{
    public abstract T ConvertFromSurrogate();
    public abstract void ConvertToSurrogate(T value);
}

// Aggregate用サロゲート
[GenerateSerializer]
public class DaprAggregateSurrogate : DaprSurrogate<IAggregateCommon>
{
    [Id(0)] public byte[] CompressedPayload { get; set; }
    [Id(1)] public string PayloadTypeName { get; set; }
    [Id(2)] public int Version { get; set; }
    [Id(3)] public Dictionary<string, string> Metadata { get; set; }
}
```

#### 1.2 型登録システム

```csharp
// 型登録インターフェース
public interface IDaprTypeRegistry
{
    void RegisterType<T>(string typeAlias) where T : class;
    Type? ResolveType(string typeAlias);
    string GetTypeAlias(Type type);
}

// Source Generatorで生成
[GeneratedCode("Sekiban.Pure.SourceGenerator", "1.0.0")]
public static class DaprGeneratedTypeRegistry
{
    public static void RegisterAll(IDaprTypeRegistry registry)
    {
        // 自動生成されるコード
        registry.RegisterType<CreateUser>("CreateUser");
        registry.RegisterType<UserCreated>("UserCreated");
        // ...
    }
}
```

### 2. 圧縮とバイナリシリアライゼーション

#### 2.1 圧縮ユーティリティ

```csharp
public static class DaprCompressionUtility
{
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(data);
        }
        return output.ToArray();
    }
    
    public static byte[] Decompress(byte[] compressedData)
    {
        using var input = new MemoryStream(compressedData);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
```

#### 2.2 シリアライゼーションサービス

```csharp
public interface IDaprSerializationService
{
    ValueTask<byte[]> SerializeAsync<T>(T value);
    ValueTask<T?> DeserializeAsync<T>(byte[] data);
    ValueTask<DaprAggregateSurrogate> SerializeAggregateAsync(IAggregateCommon aggregate);
    ValueTask<IAggregateCommon?> DeserializeAggregateAsync(DaprAggregateSurrogate surrogate);
}

public class DaprSerializationService : IDaprSerializationService
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IDaprTypeRegistry _typeRegistry;
    
    public async ValueTask<DaprAggregateSurrogate> SerializeAggregateAsync(IAggregateCommon aggregate)
    {
        // 1. JSONシリアライズ
        var json = JsonSerializer.SerializeToUtf8Bytes(aggregate.GetPayload(), _jsonOptions);
        
        // 2. 圧縮
        var compressed = DaprCompressionUtility.Compress(json);
        
        // 3. サロゲート作成
        return new DaprAggregateSurrogate
        {
            CompressedPayload = compressed,
            PayloadTypeName = _typeRegistry.GetTypeAlias(aggregate.GetPayload().GetType()),
            Version = aggregate.Version,
            Metadata = aggregate.GetMetadata()
        };
    }
}
```

### 3. Actor統合

#### 3.1 改善されたAggregateActor

```csharp
[Actor(TypeName = "AggregateActor")]
public class AggregateActor : Actor, IAggregateActor
{
    private readonly IDaprSerializationService _serialization;
    private readonly ISekibanCommandExecutor _commandExecutor;
    
    protected override async Task OnActivateAsync()
    {
        // アクター初期化時の型登録
        DaprGeneratedTypeRegistry.RegisterAll(_typeRegistry);
        await base.OnActivateAsync();
    }
    
    public async Task<CommandResponse> ExecuteCommandAsync(DaprCommandEnvelope envelope)
    {
        // 1. コマンドのデシリアライズ
        var command = await _serialization.DeserializeAsync<ICommandWithHandlerSerializable>(
            envelope.CommandData);
        
        // 2. 現在の状態を取得
        var surrogate = await StateManager.GetStateAsync<DaprAggregateSurrogate>("aggregate");
        var aggregate = surrogate != null 
            ? await _serialization.DeserializeAggregateAsync(surrogate)
            : null;
        
        // 3. コマンド実行
        var result = await _commandExecutor.ExecuteAsync(command, aggregate);
        
        // 4. 新しい状態を保存
        if (result.IsSuccess)
        {
            var newSurrogate = await _serialization.SerializeAggregateAsync(result.Aggregate);
            await StateManager.SetStateAsync("aggregate", newSurrogate);
            await StateManager.SaveStateAsync();
        }
        
        return result.ToCommandResponse();
    }
}
```

#### 3.2 コマンドエンベロープ

```csharp
[GenerateSerializer]
public class DaprCommandEnvelope
{
    [Id(0)] public byte[] CommandData { get; set; }
    [Id(1)] public string CommandType { get; set; }
    [Id(2)] public Dictionary<string, string> Headers { get; set; }
    [Id(3)] public string CorrelationId { get; set; }
}
```

### 4. イベントストア統合

#### 4.1 改善されたDaprEventStore

```csharp
public class DaprEventStore : ISekibanRepository
{
    private readonly DaprClient _daprClient;
    private readonly IDaprSerializationService _serialization;
    
    public async Task<ResultBox<EventDocumentWithBlobData>> SaveEvent(
        EventDocument eventDocument)
    {
        // イベントをバイナリシリアライズ
        var eventData = await _serialization.SerializeAsync(eventDocument.Event);
        
        var daprEvent = new DaprEventEnvelope
        {
            EventId = eventDocument.Id,
            EventData = eventData,
            EventType = _typeRegistry.GetTypeAlias(eventDocument.Event.GetType()),
            AggregateId = eventDocument.AggregateId,
            Version = eventDocument.Version,
            Timestamp = eventDocument.Timestamp,
            Metadata = eventDocument.Metadata
        };
        
        // Dapr State Storeに保存
        var key = $"event:{eventDocument.AggregateId}:{eventDocument.Version}";
        await _daprClient.SaveStateAsync("eventstore", key, daprEvent);
        
        // PubSubでイベント配信
        await _daprClient.PublishEventAsync("events", eventDocument.Event.GetType().Name, daprEvent);
        
        return ResultBox.FromValue(eventDocument);
    }
}
```

### 5. 設定と起動

#### 5.1 DI設定

```csharp
public static class DaprSerializationExtensions
{
    public static IServiceCollection AddSekibanDaprSerialization(
        this IServiceCollection services,
        Action<DaprSerializationOptions>? configure = null)
    {
        var options = new DaprSerializationOptions();
        configure?.Invoke(options);
        
        services.AddSingleton<IDaprTypeRegistry, DaprTypeRegistry>();
        services.AddSingleton<IDaprSerializationService, DaprSerializationService>();
        
        // Source Generatorで生成された型を登録
        services.AddSingleton(sp =>
        {
            var registry = sp.GetRequiredService<IDaprTypeRegistry>();
            DaprGeneratedTypeRegistry.RegisterAll(registry);
            return registry;
        });
        
        // JSON設定
        services.ConfigureJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new DaprTypeConverterFactory());
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });
        
        return services;
    }
}
```

#### 5.2 使用例

```csharp
var builder = WebApplication.CreateBuilder(args);

// Daprサービス
builder.Services.AddDapr();
builder.Services.AddActors(options =>
{
    options.Actors.RegisterActor<AggregateActor>();
    options.JsonSerializerOptions = DaprSerializationOptions.Default;
});

// Sekiban + Dapr
builder.Services.AddSekibanWithDapr(options =>
{
    options.UseCompression = true;
    options.CompressionLevel = CompressionLevel.Optimal;
});

// シリアライゼーション設定
builder.Services.AddSekibanDaprSerialization(options =>
{
    options.EnableTypeAliases = true;
    options.EnableCompression = true;
});
```

### 6. パフォーマンス最適化

#### 6.1 キャッシュ層

```csharp
public class CachedDaprSerializationService : IDaprSerializationService
{
    private readonly IDaprSerializationService _inner;
    private readonly IMemoryCache _cache;
    
    public async ValueTask<T?> DeserializeAsync<T>(byte[] data)
    {
        var cacheKey = $"deser:{typeof(T).Name}:{Convert.ToBase64String(data.Take(32).ToArray())}";
        
        if (_cache.TryGetValue<T>(cacheKey, out var cached))
        {
            return cached;
        }
        
        var result = await _inner.DeserializeAsync<T>(data);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        
        return result;
    }
}
```

#### 6.2 バッチ処理

```csharp
public class BatchedDaprEventStore : ISekibanRepository
{
    private readonly Channel<EventDocument> _eventChannel;
    
    public async Task<ResultBox<EventDocumentWithBlobData>> SaveEvent(
        EventDocument eventDocument)
    {
        // チャネルにイベントを追加
        await _eventChannel.Writer.WriteAsync(eventDocument);
        
        // バックグラウンドでバッチ処理
        _ = ProcessBatchAsync();
        
        return ResultBox.FromValue(eventDocument);
    }
    
    private async Task ProcessBatchAsync()
    {
        var batch = new List<EventDocument>();
        
        while (await _eventChannel.Reader.WaitToReadAsync())
        {
            while (_eventChannel.Reader.TryRead(out var evt) && batch.Count < 100)
            {
                batch.Add(evt);
            }
            
            if (batch.Count > 0)
            {
                // バッチでDaprに保存
                await SaveBatchToDapr(batch);
                batch.Clear();
            }
        }
    }
}
```

### 7. マイグレーション戦略

#### 7.1 段階的移行

1. **Phase 1**: 新しいシリアライゼーションサービスの実装
2. **Phase 2**: 既存データの読み取り互換性を維持
3. **Phase 3**: バックグラウンドでのデータマイグレーション
4. **Phase 4**: 古いシリアライゼーションコードの削除

#### 7.2 互換性レイヤー

```csharp
public class CompatibilityDaprSerializationService : IDaprSerializationService
{
    public async ValueTask<IAggregateCommon?> DeserializeAggregateAsync(
        DaprAggregateSurrogate surrogate)
    {
        try
        {
            // 新形式で試行
            return await _newSerializer.DeserializeAggregateAsync(surrogate);
        }
        catch
        {
            // 旧形式にフォールバック
            return await _legacySerializer.DeserializeAggregateAsync(surrogate);
        }
    }
}
```

## まとめ

この再設計により：

1. **パフォーマンス向上**: 圧縮とバイナリシリアライゼーション
2. **型安全性**: コンパイル時の型チェックとSource Generator
3. **Orleans互換性**: 同様のSurrogateパターン採用
4. **保守性**: 明確な抽象化と拡張ポイント
5. **移行容易性**: 段階的移行と互換性レイヤー

実装は段階的に行い、既存システムへの影響を最小限に抑えながら、パフォーマンスと機能を改善します。

## 追加要件: Protobuf対応

コマンド、イベントの永続化に関して、規定の方法を使っても解決できないことがわかりました。
ですので、protobuf で管理したいと思います。

基本的には名称が被らないと言う前提で、　protobuf で管理するようにしたいと思っています。
コマンドに送る時、コマンドから受けるアクターの中でも、protobuf で渡しますが、actorは基本的にJSONなので、エンベロープとJSONバイナリの独自の決まった型で送り、SekibanDaprExecutorにはそのままCommandで対応できるようにしたいと思います。

将来的にはマルチ言語に対応したいのですが、Projector とコマンドのセットを登録した複数言語の中からマッチして相互呼び出しする機構も必要かもしれませんが、今はC#限定の機能として作っていただいて構いません。

イベントの保存に関しては、Protobufで送ったら、そのまま保存してもいいかもしれませんし、cosmosなどに保存する場合は、エンベロープとJSONで保存しておけば、それからマッピングしてprotobufでイベントハンドラーとプロジェクターで送信できるかと思います。
