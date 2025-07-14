# Sekiban MinimalAPI PubSub Event Relay

## 概要

Sekiban.Pure.Daprでは、PubSubイベントをMultiProjectorActorに中継するためのMinimalAPI拡張メソッドを提供しています。
これにより、ライブラリの利用者が明示的にエンドポイントを有効化する必要がある（opt-in方式）ため、意図しない重複処理を避けることができます。

## 基本的な使用方法

### 1. 基本的なリレーエンドポイント

```csharp
// Program.cs
using Sekiban.Pure.Dapr.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Sekiban と Dapr の設定
builder.Services.AddSekibanWithDapr(options =>
{
    // 設定
});

var app = builder.Build();

// PubSubイベントリレーエンドポイントを明示的に有効化
app.MapSekibanEventRelay();

app.Run();
```

### 2. カスタムオプション付きリレー

```csharp
// Program.cs
app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    PubSubName = "my-pubsub",
    TopicName = "my-events",
    EndpointPath = "/custom/pubsub/events",
    ConsumerGroup = "my-consumer-group",
    MaxConcurrency = 5,
    ContinueOnProjectorFailure = false
});
```

### 3. 設定ベースでの条件付き有効化

```csharp
// Program.cs
app.MapSekibanEventRelayIfEnabled(options =>
{
    options.Enabled = builder.Configuration.GetValue<bool>("Sekiban:PubSub:Enabled");
    options.PubSubName = builder.Configuration.GetValue<string>("Sekiban:PubSub:ComponentName") ?? "sekiban-pubsub";
    options.ConsumerGroup = builder.Configuration.GetValue<string>("Sekiban:PubSub:ConsumerGroup");
});
```

### 4. 開発環境でのみ有効化

```csharp
// Program.cs
app.MapSekibanEventRelayForDevelopment(
    app.Environment.IsDevelopment(),
    new SekibanPubSubRelayOptions
    {
        EndpointPath = "/dev/pubsub/events"
    });
```

### 5. 複数トピックへの対応

```csharp
// Program.cs
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
```

## 設定オプション

### SekibanPubSubRelayOptions

| プロパティ | デフォルト値 | 説明 |
|-----------|------------|------|
| `Enabled` | `true` | リレー機能を有効にするかどうか |
| `PubSubName` | `"sekiban-pubsub"` | PubSubコンポーネント名 |
| `TopicName` | `"events.all"` | 購読するトピック名 |
| `EndpointPath` | `"/internal/pubsub/events"` | エンドポイントのパス |
| `ContinueOnProjectorFailure` | `true` | 個別プロジェクターの失敗時に処理を続行するかどうか |
| `ConsumerGroup` | `null` | Consumer Group名（Dapr 1.14+でサポート） |
| `MaxConcurrency` | `10` | 最大並行処理数 |
| `EnableDeadLetterQueue` | `false` | デッドレターキューを有効にするかどうか |
| `DeadLetterTopic` | `"events.dead-letter"` | デッドレターキューのトピック名 |
| `MaxRetryCount` | `3` | リトライの最大回数 |

## スケーリングの考慮事項

### Consumer Group の使用（推奨）

```csharp
// 本番環境での推奨設定
app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    ConsumerGroup = "sekiban-projectors",
    MaxConcurrency = 20,
    ContinueOnProjectorFailure = false,
    EnableDeadLetterQueue = true
});
```

### 環境別設定

```csharp
// appsettings.json
{
  "Sekiban": {
    "PubSub": {
      "Enabled": true,
      "ComponentName": "sekiban-pubsub",
      "ConsumerGroup": "sekiban-projectors-prod",
      "MaxConcurrency": 50
    }
  }
}

// Program.cs
app.MapSekibanEventRelayIfEnabled(options =>
{
    var config = builder.Configuration.GetSection("Sekiban:PubSub");
    options.Enabled = config.GetValue<bool>("Enabled");
    options.PubSubName = config.GetValue<string>("ComponentName") ?? "sekiban-pubsub";
    options.ConsumerGroup = config.GetValue<string>("ConsumerGroup");
    options.MaxConcurrency = config.GetValue<int>("MaxConcurrency", 10);
});
```

## 移行ガイド

### 従来のControllerからの移行

**従来の方法（非推奨）:**
```csharp
// EventPubSubController が自動的に登録される
// 明示的な制御ができない
```

**新しい方法（推奨）:**
```csharp
// 明示的にエンドポイントを有効化
app.MapSekibanEventRelay();
```

### 段階的移行

1. **段階1**: 新しいMinimalAPIエンドポイントを追加
2. **段階2**: 新しいエンドポイントでの動作を確認
3. **段階3**: 古いControllerの使用を停止
4. **段階4**: 古いControllerを削除

## エラー処理

### 個別プロジェクターの失敗処理

```csharp
app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    ContinueOnProjectorFailure = false, // 1つでも失敗したら全体を失敗させる
    MaxRetryCount = 5,
    EnableDeadLetterQueue = true
});
```

### デッドレターキューの有効化

```csharp
app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    EnableDeadLetterQueue = true,
    DeadLetterTopic = "events.failed",
    MaxRetryCount = 3
});
```

## セキュリティ考慮事項

### 内部エンドポイントの保護

```csharp
app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    EndpointPath = "/internal/pubsub/events"
})
.RequireHost("localhost") // 本番環境では適切な制限を設定
.WithMetadata("ExcludeFromDescription", true); // API文書から除外
```

### 認証・認可

```csharp
app.MapSekibanEventRelay()
.RequireAuthorization("DaprInternalOnly"); // 適切な認可ポリシーを設定
```

## 監視とログ

MinimalAPI拡張メソッドは詳細なログを提供します：

- イベント受信ログ
- プロジェクター処理ログ
- エラー詳細ログ
- パフォーマンス指標

```csharp
// appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Sekiban.Pure.Dapr.Extensions.SekibanEventRelayHandler": "Debug"
    }
  }
}
```

## よくある質問

### Q: 古いEventPubSubControllerはいつ削除されますか？
A: 次のメジャーバージョンで削除予定です。新しいMinimalAPI方式への移行を推奨します。

### Q: Consumer Groupを使用しないとどうなりますか？
A: 複数インスタンスで同じイベントを重複処理する可能性があります。

### Q: 1つのアプリケーションで複数のトピックを購読できますか？
A: はい、`MapSekibanEventRelayMultiTopic`を使用してください。

### Q: カスタムプロジェクターフィルタリングは可能ですか？
A: 現在はサポートされていませんが、将来のバージョンで追加予定です。
