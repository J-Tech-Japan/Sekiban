# Sekiban PubSub MinimalAPI リレーの使用方法

## 概要

Sekiban.Pure.Daprは、PubSubイベントをMultiProjectorActorに中継するためのMinimalAPI拡張メソッドを提供します。
この方式では、ライブラリ利用者が明示的にエンドポイントを有効化する必要があり（opt-in方式）、意図しない重複処理を避けることができます。

## 基本的な使用方法

### 1. シンプルな有効化

```csharp
var builder = WebApplication.CreateBuilder(args);

// Sekibanコアサービスを追加
builder.Services.AddSekibanWithDapr();

var app = builder.Build();

// PubSubリレーを明示的に有効化（opt-in）
app.MapSekibanEventRelay();

app.Run();
```

### 2. 設定をカスタマイズ

```csharp
var app = builder.Build();

// カスタム設定でリレーを有効化
app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    PubSubName = "my-pubsub",
    TopicName = "domain.events",
    EndpointPath = "/api/internal/events",
    ConsumerGroup = "my-consumer-group", // 重複処理防止
    MaxConcurrency = 20,
    ContinueOnProjectorFailure = false // プロジェクター失敗時に全体を失敗させる
});

app.Run();
```

### 3. 設定ファイルベースの制御

```csharp
var app = builder.Build();

// appsettings.jsonの設定に基づいて有効化
app.MapSekibanEventRelayIfEnabled(options =>
{
    var config = app.Configuration.GetSection("Sekiban:PubSub");
    options.Enabled = config.GetValue<bool>("Enabled");
    options.PubSubName = config.GetValue<string>("PubSubName") ?? "sekiban-pubsub";
    options.TopicName = config.GetValue<string>("TopicName") ?? "events.all";
    options.EndpointPath = config.GetValue<string>("EndpointPath") ?? "/internal/pubsub/events";
    options.ConsumerGroup = config.GetValue<string>("ConsumerGroup");
});

app.Run();
```

### 4. 開発環境でのみ有効化

```csharp
var app = builder.Build();

// 開発環境でのみPubSubリレーを有効化
app.MapSekibanEventRelayForDevelopment(
    app.Environment.IsDevelopment(),
    new SekibanPubSubRelayOptions
    {
        EndpointPath = "/dev/pubsub/events"
    });

app.Run();
```

### 5. 複数トピックへの対応

```csharp
var app = builder.Build();

// 複数のトピックに対してリレーを設定
app.MapSekibanEventRelayMultiTopic(
    new SekibanPubSubRelayOptions
    {
        PubSubName = "sekiban-pubsub",
        TopicName = "events.customer",
        EndpointPath = "/pubsub/customer-events",
        ConsumerGroup = "customer-projectors"
    },
    new SekibanPubSubRelayOptions
    {
        PubSubName = "sekiban-pubsub",
        TopicName = "events.order",
        EndpointPath = "/pubsub/order-events",
        ConsumerGroup = "order-projectors"
    }
);

app.Run();
});

app.Run();
```

```json
// appsettings.json
{
  "Sekiban": {
    "PubSub": {
      "Enabled": true,
      "PubSubName": "sekiban-pubsub",
      "TopicName": "events.all",
      "EndpointPath": "/internal/pubsub/events"
    }
  }
}
```

### 4. 環境別設定

```csharp
var app = builder.Build();

// 開発環境では無効、本番環境では有効
if (app.Environment.IsProduction())
{
    app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
    {
        PubSubName = "prod-sekiban-pubsub",
        TopicName = "events.all",
        EndpointPath = "/internal/pubsub/events",
        ContinueOnProjectorFailure = true
    });
}
else if (app.Environment.IsDevelopment())
{
    // 開発環境では詳細ログ付きで有効化
    app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
    {
        PubSubName = "dev-sekiban-pubsub",
        TopicName = "events.all",
        EndpointPath = "/dev/pubsub/events"
    });
}

app.Run();
```

## Consumer Group設定

MinimalAPIと合わせてConsumer Groupを設定してスケールアウト対応：

```yaml
# subscription.yaml
apiVersion: dapr.io/v2alpha1
kind: Subscription
metadata:
  name: sekiban-events-subscription
spec:
  topic: events.all
  routes:
    default: /internal/pubsub/events  # EndpointPathと一致させる
  pubsubname: sekiban-pubsub
  metadata:
    consumerGroup: "my-app-projectors"  # アプリ固有のConsumer Group
scopes:
- my-app-name
```

## ログ出力例

```
info: SekibanEventRelayHandler[0]
      Received event envelope: AggregateId=123e4567-e89b-12d3-a456-426614174000, Version=1, Endpoint=/internal/pubsub/events

debug: SekibanEventRelayHandler[0]
       Forwarded event to projector: UserProjector

debug: SekibanEventRelayHandler[0]
       Forwarded event to projector: OrderStatisticsProjector

debug: SekibanEventRelayHandler[0]
       Successfully processed event 456e7890-e89b-12d3-a456-426614174001 for 2 projectors
```

## エラーハンドリング

```json
// 正常レスポンス
{
  "message": "Event processed successfully",
  "eventId": "123e4567-e89b-12d3-a456-426614174000"
}

// エラーレスポンス
{
  "title": "Event processing failed",
  "detail": "Failed to deserialize event envelope",
  "status": 500
}
```

## マイグレーション

従来のEventPubSubControllerから移行する場合：

```csharp
// 従来（自動有効化される）
// EventPubSubControllerが自動的に /pubsub/events を公開

// 新方式（明示的有効化）
app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    EndpointPath = "/pubsub/events" // 既存のパスを維持
});
```

## 利点

1. **opt-in方式**: ライブラリを参照しただけでは有効にならない ✅
2. **設定による制御**: 環境やアプリケーションごとに細かく制御可能 ⚙️
3. **シンプルなAPI**: MinimalAPIで軽量 🪶
4. **Controllerレス**: Controllerクラスが不要 🚫
5. **Consumer Group対応**: スケールアウト時の重複処理を防止 🛡️
6. **将来対応**: Dapr 1.17でActor直接購読が可能になった際の移行が容易 🔮
