# スケールアウト時の重複処理対策

## 問題の概要

EventPubSubControllerをライブラリで提供すると、クライアントアプリケーションが複数インスタンスでスケールアウトした際に、同じイベントが複数回処理される問題が発生します。

## Dapr PubSubの制約（ChatGPT調査結果） 🔍

**重要な発見**：
- **現行Dapr（v1.15-1.16）では、PubSub → Actor への直接配信はサポートされていない** ❌
- Dapr SidecarはトピックをSubscribeすると、必ず**HTTPまたはgRPCのアプリケーションエンドポイント**にメッセージをPOST
- "Act## 推奨アプローチ

ChatGPTからの調査結果に基づく最新の優先順位：

1. **"リレー"エンドポイント + 最小API パターン** を最優先で実装 🎯
   - HTTP経由は避けられないが、最小限のリレーで実現
   - ライブラリは拡張メソッドのみ提供（opt-in）
   - Controllerクラス不要

2. **Consumer Group** による重複処理防止 🛡️
   - 同一`app-id`での自動重複回避
   - 明示的なConsumer Group設定

3. **Streaming Subscription（α機能）** の将来対応準備 🔮
   - Dapr 1.17以降でActor直接購読が可能になる予定
   - `IEventDispatcher`抽象化で実装差し替えに備える

4. **Assembly分離パターン** は次善策 📦
   - リレーパターンで解決できない場合の代替案は2019年に提案（Issue #501）されたが、まだ実装されていない（Milestone v1.17）

| やりたいこと | 可否 | 補足 |
|--------------|------|------|
| PubSubメッセージをActorに**直接**ルーティング | **✗ 不可** | SidecarはActorランタイムの内部キューを認識しない |
| Actor自身が`subscribe`宣言 | **✗ 不可** | 現状、`dapr/actors` APIはPubSubを扱わない |
| HTTPコントローラーを置かずに受信 | **△** | 最小APIでprivateルート、またはgRPCストリーミング（α）を使用 |

## 解決策

### 0. "リレー"エンドポイント + 最小API パターン（ChatGPT推奨）🌟🌟🌟

HTTP経由は必要だが、最小限のリレーエンドポイントでActorに転送：

```csharp
// ライブラリ側：拡張メソッドで提供
public static class SekibanEventRelayExtensions
{
    public static IEndpointRouteBuilder MapSekibanEventRelay(
        this IEndpointRouteBuilder app, 
        string topicName = "events.all",
        string pubsubName = "sekiban-pubsub")
    {
        app.MapPost("/internal/pubsub/events",
            async (DaprEventEnvelope envelope, IActorProxyFactory actorFactory, SekibanDomainTypes domainTypes) =>
            {
                var projectorNames = domainTypes.MultiProjectorsType.GetAllProjectorNames();
                
                var tasks = projectorNames.Select(async projectorName =>
                {
                    var actorId = new ActorId(projectorName);
                    var actor = actorFactory.CreateActorProxy<IMultiProjectorActor>(
                        actorId, nameof(MultiProjectorActor));
                    await actor.HandlePublishedEvent(envelope);
                });
                
                await Task.WhenAll(tasks);
                return Results.Ok();
            })
            .WithTopic(pubsubName, topicName)  // Daprトピック登録
            .WithOpenApi(operation => operation.WithTags("Internal")); // 内部API扱い
            
        return app;
    }
}
```

クライアント側：
```csharp
var builder = WebApplication.CreateBuilder(args);

// コアライブラリは常に追加
builder.Services.AddSekibanCore();

var app = builder.Build();

// PubSubリレーが必要な場合のみ明示的に追加（opt-in）
if (builder.Configuration.GetValue<bool>("Sekiban:EnablePubSub"))
{
    app.MapSekibanEventRelay(
        topicName: "events.all",
        pubsubName: "sekiban-pubsub");
}

app.Run();
```

**メリット**：
- ルートは1本のみ `/internal/pubsub/events` 🎯
- Controllerクラス不要 ✅
- ライブラリ側は拡張メソッドのみ提供 ✅
- opt-in方式で自動有効化されない ✅

### 1. Assembly分離パターン（従来案）🌟🌟

最も根本的な解決策として、Controllerを別アセンブリに分離する：

```
Sekiban.Pure.Dapr          // コアライブラリ（Controllerなし）
Sekiban.Pure.Dapr.AspNetCore  // Controller専用ライブラリ
```

```csharp
// クライアント側での明示的な有効化
var builder = WebApplication.CreateBuilder(args);

// コアライブラリは常に追加
builder.Services.AddSekibanCore();

// PubSubエンドポイントが必要な場合のみ明示的に追加
if (builder.Configuration.GetValue<bool>("Sekiban:EnablePubSub"))
{
    builder.Services.AddSekibanPubSub(options =>
    {
        options.ConsumerGroup = builder.Configuration["Sekiban:ConsumerGroup"] 
                                ?? builder.Environment.ApplicationName;
        options.EnableController = true;
    });
}
```

### 2. Consumer Group（推奨）🌟

```yaml
# subscription.yaml
apiVersion: dapr.io/v2alpha1
kind: Subscription
metadata:
  name: domain-events-subscription
spec:
  topic: events.all
  routes:
    default: /pubsub/events
  pubsubname: sekiban-pubsub
  metadata:
    consumerGroup: "sekiban-projectors"  # 同一グループは1つのインスタンスのみが受信
scopes:
- your-app-name
```

**ChatGPTからの重要な指摘：**
- 同じ`app-id`を共有する全レプリカは、Daprが自動的に1つのインスタンスのみにメッセージを配信
- しかし、ブローカーがConsumer Groupをサポートしていない場合は重複配信が発生する可能性がある
- プロダクション環境では必ずConsumer Group対応のプロバイダー（Kafka、Azure Service Bus、RabbitMQ、Redis Streams）を使用

### 3. Streaming Subscription（α機能）

```csharp
// gRPCストリーミングを使用（Dapr 1.14+ α機能）
builder.Services.AddDaprStreamSubscriber(options =>
{
    options.AddSubscription("sekiban-pubsub", "events.all", async (envelope) =>
    {
        // Actor呼び出し処理
        var actorFactory = serviceProvider.GetRequiredService<IActorProxyFactory>();
        // ...
    });
});
```

**注意**：
- .NETではまだプレビュー機能
- HTTPを使わないがホスト側での登録が必要

### 4. Minimal API パターン（Internal Controller回避）

```csharp
// Controllerを内部クラスにしてMVC自動発見を回避
internal class EventPubSubController : ControllerBase
{
    // ...実装
}

// 代わりにMinimal APIで明示的にエンドポイントを公開
public static class SekibanPubSubEndpoints
{
    public static IEndpointRouteBuilder MapSekibanPubSubEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/pubsub/events", 
            [Topic("sekiban-pubsub", "events.all")]
            async (DaprEventEnvelope envelope, IEventPubSubHandler handler) =>
            {
                return await handler.HandleEventAsync(envelope);
            });
            
        return builder;
    }
}
```

クライアント側：
```csharp
var app = builder.Build();

// 明示的にPubSubエンドポイントを有効化
if (builder.Configuration.GetValue<bool>("Sekiban:EnablePubSub"))
{
    app.MapSekibanPubSubEndpoints();
}
```

### 5. Application Parts制御パターン

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSekibanPubSub(
        this IServiceCollection services, 
        Action<SekibanPubSubOptions>? configure = null)
    {
        var options = new SekibanPubSubOptions();
        configure?.Invoke(options);
        
        services.Configure<SekibanPubSubOptions>(opt => 
        {
            opt.EnableController = options.EnableController;
            opt.ConsumerGroup = options.ConsumerGroup;
        });
        
        // Controller有効時のみApplicationPartを追加
        if (options.EnableController)
        {
            services.AddControllers()
                .ConfigureApplicationPartManager(apm =>
                {
                    apm.ApplicationParts.Add(
                        new AssemblyPart(typeof(EventPubSubController).Assembly));
                });
        }
        else
        {
            // 無効時はApplicationPartから除去
            services.AddControllers()
                .ConfigureApplicationPartManager(apm =>
                {
                    var partToRemove = apm.ApplicationParts
                        .FirstOrDefault(p => p.Name == typeof(EventPubSubController).Assembly.GetName().Name);
                    if (partToRemove != null)
                    {
                        apm.ApplicationParts.Remove(partToRemove);
                    }
                });
        }
        
        return services;
    }
}

public class SekibanPubSubOptions
{
    public bool EnableController { get; set; } = false;
    public string ConsumerGroup { get; set; } = "default";
    public RetryPolicy UnhandledExceptionPolicy { get; set; } = RetryPolicy.ExponentialBackoff();
}
```

### 6. Aggregate IDベースのルーティング

```csharp
[Topic("sekiban-pubsub", "events.all")]
[HttpPost("events")]
public async Task<IActionResult> HandleEvent([FromBody] DaprEventEnvelope envelope)
{
    // Aggregate IDのハッシュ値でインスタンスを決定
    var instanceId = Environment.GetEnvironmentVariable("INSTANCE_ID") ?? "0";
    var expectedInstance = (envelope.AggregateId.GetHashCode() % totalInstances).ToString();
    
    if (instanceId != expectedInstance)
    {
        // このインスタンスでは処理しない
        return Ok(); 
    }
    
    // 通常の処理
    // ...
}
```

### 7. 冪等性の強化

```csharp
public async Task HandlePublishedEvent(DaprEventEnvelope envelope)
{
    // より厳密な重複チェック
    var lockKey = $"event_lock_{envelope.SortableUniqueId}";
    
    using var distributedLock = await AcquireDistributedLock(lockKey);
    if (distributedLock == null)
    {
        // 他のインスタンスで処理中
        return;
    }
    
    if (await IsSortableUniqueIdReceived(envelope.SortableUniqueId))
    {
        return; // 既に処理済み
    }
    
    // 処理実行
    // ...
}
```

### 8. Single Instance Deployment

```yaml
# deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: sekiban-projector-service
spec:
  replicas: 1  # プロジェクターサービスは単一インスタンス
  selector:
    matchLabels:
      app: sekiban-projector
  template:
    metadata:
      labels:
        app: sekiban-projector
    spec:
      containers:
      - name: projector
        image: your-projector-service:latest
```

## 推奨アプローチ

ChatGPTからの提案に基づく優先順位：

1. **Assembly分離パターン** を最優先で実装 🎯
   - `Sekiban.Pure.Dapr.AspNetCore`として分離
   - クライアント側での明示的な有効化
   
2. **Consumer Group** による重複処理防止 🛡️
   - 同一`app-id`での自動重複回避
   - 明示的なConsumer Group設定

3. **Minimal API + Internal Controller** のハイブリッド �
   - 既存Controllerを`internal`に変更
   - `MapSekibanPubSubEndpoints()`での明示的公開

4. **Application Parts制御** による細かい制御 ⚙️
   - `ConfigureApplicationPartManager`での動的制御
   - 設定ベースの有効/無効切り替え

## 将来への対応準備

```csharp
// 抽象化インターフェースで実装差し替えに備える
public interface IEventDispatcher
{
    Task DispatchEventAsync(DaprEventEnvelope envelope);
}

// 現行実装（HTTPリレー経由）
public class HttpRelayEventDispatcher : IEventDispatcher
{
    public async Task DispatchEventAsync(DaprEventEnvelope envelope)
    {
        // Actor呼び出し処理
    }
}

// 将来実装（Direct Actor Subscription - Dapr 1.17+）
public class DirectActorEventDispatcher : IEventDispatcher
{
    public async Task DispatchEventAsync(DaprEventEnvelope envelope)
    {
        // Actor直接購読処理（将来実装）
    }
}
```

## Dapr Issue #501 の進捗監視

- **GitHub Issue**: [dapr/dapr#501](https://github.com/dapr/dapr/issues/501)
- **予定**: Milestone v1.17
- **内容**: "Actor が Subscriber になる" 機能
- **影響**: HTTPエンドポイント不要でActor直接購読が可能になる

ChatGPTが提案する追加の改善点：

```csharp
// ❶ シンプルな設定ベースの制御
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSekibanCore();

// appsettings.jsonでの制御
if (builder.Configuration.GetValue<bool>("Sekiban:EnablePubSub"))
{
    builder.Services.AddSekibanPubSub(options =>
    {
        options.ConsumerGroup = builder.Configuration["Sekiban:ConsumerGroup"] 
                                ?? builder.Environment.ApplicationName;
        options.UnhandledExceptionPolicy = RetryPolicy.ExponentialBackoff();
    });
}

var app = builder.Build();
app.MapSekibanPubSubEndpoints(); // 設定に基づく自動判定
app.Run();
```

```json
// appsettings.json
{
  "Sekiban": {
    "EnablePubSub": true,
    "ConsumerGroup": "user-service-projectors"
  }
}
```

## 実装状況（更新）

**✅ 完了**：
- **MinimalAPI リレーエンドポイント** の実装 🎯
  - `SekibanEventRelayExtensions.MapSekibanEventRelay()`
  - `MapSekibanEventRelayIfEnabled()` (設定ベース制御)
  - `MapSekibanEventRelayForDevelopment()` (開発環境限定)
  - `MapSekibanEventRelayMultiTopic()` (複数トピック対応)
- **拡張メソッド**でのopt-in方式採用
- **Consumer Group**対応
- **従来Controller**のDeprecated化とWarning追加
- **詳細な使用ガイド**の作成

**📝 ドキュメント作成済み**：
- `README_MinimalAPI_PubSub.md` - 詳細な使用ガイド
- `PubSubMinimalApiUsage.md` - 基本的な使用方法 (更新)
- `Examples/Program.MinimalAPI.cs` - サンプルコード
- `appsettings.example.json` - 設定例

**🎯 推奨使用方法**：
```csharp
// 基本的な使用
app.MapSekibanEventRelay();

// 本番環境推奨設定
app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    ConsumerGroup = "sekiban-projectors-prod",
    MaxConcurrency = 20,
    ContinueOnProjectorFailure = false,
    EnableDeadLetterQueue = true
});

// 設定ベース制御
app.MapSekibanEventRelayIfEnabled(options =>
{
    var config = app.Configuration.GetSection("Sekiban:PubSub");
    options.Enabled = config.GetValue<bool>("Enabled");
    options.ConsumerGroup = config.GetValue<string>("ConsumerGroup");
});
```

**重要な変更点**：
- Assembly分離は次善策に変更
- HTTPエンドポイントは現状必須（Dapr制約）
- 将来のActor直接購読に備えた抽象化が重要

## 実装上の注意

- Redis、Azure Service Bus等のPubSubプロバイダーはConsumer Groupをサポート
- In-memoryプロバイダーはサポートしていない場合がある
- プロダクション環境では必ずConsumer Group対応のプロバイダーを使用

## テスト方法

```bash
# 複数インスタンスを起動してテスト
dapr run --app-id app1 --app-port 5001 -- dotnet run --urls="http://localhost:5001"
dapr run --app-id app2 --app-port 5002 -- dotnet run --urls="http://localhost:5002"

# イベントを発行して重複処理が発生しないことを確認
curl -X POST http://localhost:5001/api/test/create-event
```
