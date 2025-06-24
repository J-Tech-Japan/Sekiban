# Sekiban.Pure.Dapr 実装方針

## 概要
Microsoft Orleans と同様の形で、Dapr (Distributed Application Runtime) 対応を追加し、Sekiban.Pure のイベントソーシング機能を Dapr の分散アプリケーション機能と統合する。

## Dapr とは
Dapr は、マイクロサービス アプリケーションの構築を簡素化するポータブルなイベント駆動型ランタイムです。
- **状態管理**: 分散キャッシュと状態ストア
- **サービス間通信**: Service Invocation と PubSub
- **アクター モデル**: Virtual Actor パターンの実装
- **ワークフロー**: 長時間実行されるプロセスの管理

## C# での Dapr 使用方法

### 1. NuGet パッケージ
```xml
<PackageReference Include="Dapr.Client" Version="1.12.0" />
<PackageReference Include="Dapr.AspNetCore" Version="1.12.0" />
<PackageReference Include="Dapr.Actors" Version="1.12.0" />
<PackageReference Include="Dapr.Actors.AspNetCore" Version="1.12.0" />
```

### 2. 基本的な使用パターン
```csharp
// DaprClient の使用
var daprClient = new DaprClientBuilder().Build();

// 状態管理
await daprClient.SaveStateAsync("statestore", "key", value);
var state = await daprClient.GetStateAsync<T>("statestore", "key");

// サービス呼び出し
var result = await daprClient.InvokeMethodAsync<Request, Response>("service", "method", request);

// PubSub
await daprClient.PublishEventAsync("pubsub", "topic", eventData);
```

### 3. アクター使用パターン
```csharp
// アクター定義
public interface IAggregateActor : IActor
{
    Task<CommandResponse> ExecuteCommandAsync(ICommandWithHandlerSerializable command);
    Task<Aggregate> GetStateAsync();
}

[Actor(TypeName = "AggregateActor")]
public class AggregateActor : Actor, IAggregateActor, IRemindable
{
    // アクター実装
}
```

## Sekiban.Pure.Dapr 実装設計

### 1. プロジェクト構造
```
src/Sekiban.Pure.Dapr/
├── Sekiban.Pure.Dapr.csproj
├── SekibanDaprExecutor.cs              // ISekibanExecutor 実装
├── Actors/
│   ├── IAggregateActor.cs              // アグリゲート アクター インターフェース
│   ├── AggregateActor.cs               // アグリゲート アクター実装
│   ├── IMultiProjectorActor.cs         // マルチプロジェクター アクター
│   └── MultiProjectorActor.cs
├── Services/
│   ├── DaprEventStore.cs               // Dapr 状態管理を使用したイベントストア
│   ├── DaprEventPublisher.cs           // Dapr PubSub を使用したイベント配信
│   └── DaprQueryService.cs             // クエリ処理サービス
├── Extensions/
│   ├── ServiceCollectionExtensions.cs  // DI 拡張
│   └── DaprExtensions.cs               // Dapr 固有の拡張
└── Configuration/
    ├── DaprSekibanOptions.cs           // 設定オプション
    └── DaprComponentsConfig.cs         // Dapr コンポーネント設定
```

### 2. メインクラス設計

#### SekibanDaprExecutor
Orleans の `SekibanOrleansExecutor` と同様の構造で、Dapr の Actor と Service Invocation を使用：

```csharp
public class SekibanDaprExecutor : ISekibanExecutor
{
    private readonly DaprClient _daprClient;
    private readonly IActorProxyFactory _actorProxyFactory;
    private readonly SekibanDomainTypes _domainTypes;
    
    public async Task<ResultBox<CommandResponse>> CommandAsync(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null)
    {
        // パーティションキーを取得
        var partitionKeys = GetPartitionKeys(command);
        
        // アクターIDを生成 (Orleans の Grain Key と同様)
        var actorId = new ActorId(partitionKeys.ToPrimaryKeysString());
        
        // アグリゲートアクターを取得
        var aggregateActor = _actorProxyFactory.CreateActorProxy<IAggregateActor>(
            actorId, 
            "AggregateActor");
            
        // コマンドを実行
        return await aggregateActor.ExecuteCommandAsync(command);
    }
}
```

#### AggregateActor
Orleans の Grain と同様の役割を持つ Dapr Actor：

```csharp
[Actor(TypeName = "AggregateActor")]
public class AggregateActor : Actor, IAggregateActor, IRemindable
{
    private readonly ISekibanRepository _repository;
    private readonly ICommandMetadataProvider _metadataProvider;
    
    public async Task<CommandResponse> ExecuteCommandAsync(
        ICommandWithHandlerSerializable command)
    {
        // Orleans Grain と同様のコマンド処理ロジック
        // 状態は Dapr State Store に保存
        
        var state = await StateManager.GetStateAsync<AggregateState>("state");
        // コマンド処理...
        await StateManager.SetStateAsync("state", newState);
        await StateManager.SaveStateAsync();
        
        return response;
    }
    
    public async Task ReceiveReminderAsync(string reminderName, byte[] state, 
        TimeSpan dueTime, TimeSpan period)
    {
        // 定期的な処理（イベント配信など）
    }
}
```

### 3. 主要な実装方針

#### 3.1 アクター ベースのアーキテクチャ
- **アグリゲート アクター**: Orleans Grain の代替として、各アグリゲートインスタンスを管理
- **マルチプロジェクター アクター**: 複数のアグリゲートからのイベントを処理
- **イベントハンドラー アクター**: イベント処理の分散実行

#### 3.2 状態管理
```csharp
public class DaprEventStore : ISekibanRepository
{
    private readonly DaprClient _daprClient;
    private const string StateStoreName = "sekiban-eventstore";
    
    public async Task<ResultBox<EventStoreDocumentWithBlobData>> GetEvents(
        PartitionKeys partitionKeys)
    {
        var key = partitionKeys.ToPrimaryKeysString();
        var events = await _daprClient.GetStateAsync<List<EventDocument>>(
            StateStoreName, 
            key);
        return events;
    }
    
    public async Task<ResultBox<EventDocumentWithBlobData>> SaveEvent(
        EventDocument eventDocument)
    {
        var key = eventDocument.PartitionKeys.ToPrimaryKeysString();
        await _daprClient.SaveStateAsync(StateStoreName, key, eventDocument);
        
        // PubSub でイベント配信
        await _daprClient.PublishEventAsync("sekiban-pubsub", "events", eventDocument);
        
        return eventDocument;
    }
}
```

#### 3.3 イベント配信
```csharp
public class DaprEventPublisher : IEventPublisher
{
    private readonly DaprClient _daprClient;
    
    public async Task PublishAsync(IEvent @event)
    {
        await _daprClient.PublishEventAsync("sekiban-pubsub", "domain-events", @event);
    }
}
```

#### 3.4 クエリ処理
```csharp
public class DaprQueryService
{
    private readonly DaprClient _daprClient;
    
    public async Task<TResult> QueryAsync<TResult>(IQueryCommon<TResult> query)
    {
        // Service Invocation を使用してクエリサービスを呼び出し
        return await _daprClient.InvokeMethodAsync<IQueryCommon<TResult>, TResult>(
            "query-service", 
            "execute-query", 
            query);
    }
}
```

### 4. 設定と起動

#### 4.1 Dapr コンポーネント設定
```yaml
# components/statestore.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-eventstore
spec:
  type: state.redis
  version: v1
  metadata:
  - name: redisHost
    value: localhost:6379

# components/pubsub.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-pubsub
spec:
  type: pubsub.redis
  version: v1
  metadata:
  - name: redisHost
    value: localhost:6379
```

#### 4.2 アプリケーション設定
```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Dapr サービス登録
builder.Services.AddDapr();
builder.Services.AddActors(options =>
{
    options.Actors.RegisterActor<AggregateActor>();
    options.Actors.RegisterActor<MultiProjectorActor>();
});

// Sekiban + Dapr 統合
builder.Services.AddSekibanWithDapr(options =>
{
    options.StateStoreName = "sekiban-eventstore";
    options.PubSubName = "sekiban-pubsub";
});

var app = builder.Build();

// Dapr ミドルウェア
app.UseRouting();
app.UseCloudEvents();
app.MapSubscribeHandler();
app.MapActorsHandlers();

app.Run();
```

### 5. Orleans との違いと利点

#### Orleans との主な違い
| 機能 | Orleans | Dapr |
|------|---------|------|
| **アクター管理** | Grain (Orleans 固有) | Actor (標準パターン) |
| **状態管理** | Orleans Persistence | Dapr State Store |
| **イベント配信** | Orleans Streams | Dapr PubSub |
| **サービス発見** | Orleans Clustering | Dapr Service Discovery |
| **言語サポート** | .NET 主体 | 多言語対応 |

#### Dapr の利点
1. **ポリアビリティ**: 複数のクラウドプロバイダーで動作
2. **多言語サポート**: .NET 以外の言語との連携が容易
3. **標準化**: CNCF の標準的なパターンを使用
4. **運用**: Kubernetes での運用が容易

### 6. 実装優先順位

#### Phase 1: 基本実装
1. `SekibanDaprExecutor` の実装
2. `AggregateActor` の基本機能
3. `DaprEventStore` の実装
4. 基本的な設定とDI

#### Phase 2: 高度な機能
1. `MultiProjectorActor` の実装
2. イベント配信と購読
3. クエリサービスの分散処理
4. リマインダーを使用した定期処理

#### Phase 3: 最適化と運用
1. パフォーマンス最適化
2. 監視とログ
3. 障害処理とリトライ
4. セキュリティ強化

### 7. 技術検討事項

#### 7.1 パフォーマンス
- アクターの配置戦略
- 状態の永続化頻度
- キャッシュ戦略

#### 7.2 一貫性
- イベントの順序保証
- 分散トランザクション
- 障害時の復旧

#### 7.3 スケーラビリティ
- アクターの水平スケーリング
- 状態ストアの分散
- ロードバランシング

この設計により、Orleans と同様の機能を Dapr で実現し、より標準的で移植性の高いイベントソーシング基盤を構築できます 🚀