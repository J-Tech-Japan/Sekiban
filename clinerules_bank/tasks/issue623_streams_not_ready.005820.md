# LLMモデル名: GitHub Copilot

# Orleans ストリームが準備できていない問題への対策計画

## 問題の概要

`src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs` の `OnActivateAsync` メソッド内で、Orleans Stream を取得する際に、ストリームの準備ができていないことが原因で `GetStreamProvider("EventStreamProvider")` が例外をスローする可能性がある。この問題は、Grainがアクティブ化される時点でストリームプロバイダーの登録が完了していないことによって発生している。

```csharp
_eventStream = this.GetStreamProvider("EventStreamProvider").GetStream<IEvent>(StreamId.Create("AllEvents", Guid.Empty));
```

## 問題の原因

Orleans では、ストリームプロバイダーの登録はサイロのスタートアップ中に非同期で行われる。そのため、早期にアクティブ化されるグレインでは、プロバイダーの準備が完了していない状況でアクセスしてしまう可能性がある。Orleans 9 では、グレインのライフサイクルステージが簡素化され（First / SetupState / Activate / Last のみ）、以前の `AfterRuntimeGrainServices` のような安全なステージが存在しなくなっている。

## 解決策の選択肢

### 選択肢A: グレインのライフサイクルを活用する方法

`ILifecycleParticipant<IGrainLifecycle>` インターフェースを実装し、ストリームの購読ロジックを `OnActivateAsync` から独立したライフサイクル参加コールバックに移動する。この方法では、Activate ステージの直後（例：`GrainLifecycleStage.Activate + 100`）に実行されるカスタムステージを追加することで、ストリームプロバイダーが準備できるまで待つことができる。

### 選択肢B: OnActivateAsyncに限定的なリトライを追加する方法

グレインの構造を変更せずに、`GetStreamProvider` の呼び出しを限定的なリトライで囲む。これにより、プロバイダーの準備ができるまで短時間待機し、再試行することができる。通常、プロバイダーは数百ミリ秒以内に登録されるため、総計約2秒までのバックオフは十分である。

### 選択肢C: サイロスタート時にプロバイダーの準備を保証する方法

サイロのスタートアップを制御できる場合、ストリームプロバイダーを早いサイロライフサイクルステージで初期化するよう強制することができる。この方法は、プロバイダーが1つだけの場合に適しており、クラスター全体でレースコンディションを解消する。

## 推奨する解決策

選択肢Aとして紹介した、グレインのライフサイクルを適切に活用する方法を推奨します。この方法は以下の利点があります：

1. Orleans 9で最も安全かつ慣用的な修正方法である
2. サードパーティのパッケージを必要とせず、Orleans標準APIのみで実装可能
3. スタートアップの速度を維持しつつ、レースコンディションを回避できる
4. コードの可読性と保守性が高い

## 具体的な実装計画

1. `MultiProjectorGrain` クラスに `ILifecycleParticipant<IGrainLifecycle>` インターフェースを実装する

```csharp
public class MultiProjectorGrain : Grain, IMultiProjectorGrain, ILifecycleParticipant<IGrainLifecycle>
```

2. `Participate` メソッドを実装してライフサイクルにサブスクライブする

```csharp
public void Participate(IGrainLifecycle lifecycle)
{
    // Activate(2000)の直後、Lastの前のステージを選択
    const int Stage = GrainLifecycleStage.Activate + 100;
    lifecycle.Subscribe<MultiProjectorGrain>(
        nameof(MultiProjectorGrain),
        Stage,
        InitStreamsAsync,
        CloseStreamsAsync);
}
```

3. ストリーム初期化ロジックを新しいメソッドに移動する

```csharp
private async Task InitStreamsAsync(CancellationToken ct)
{
    _logger.LogInformation("subscribe stream");
    _eventStream = this.GetStreamProvider("EventStreamProvider").GetStream<IEvent>(StreamId.Create("AllEvents", Guid.Empty));
    _subscription = await _eventStream.SubscribeAsync(OnStreamEventAsync, OnStreamErrorAsync, OnStreamCompletedAsync);
    
    _bootstrapping = false;
    _streamActive = true;
    _logger.LogInformation("stream active");
    FlushBuffer();
    _logger.LogInformation("stream buffer flushed");
}
```

4. クリーンアップロジックを実装する

```csharp
private Task CloseStreamsAsync(CancellationToken ct)
{
    return _subscription?.UnsubscribeAsync() ?? Task.CompletedTask;
}
```

5. `OnActivateAsync` メソッドからストリーム関連のコードを削除する

```csharp
public override async Task OnActivateAsync(CancellationToken ct)
{
    await base.OnActivateAsync(ct);

    _logger.LogInformation("restore snapshot"); 
    await _persistentState.ReadStateAsync(ct);
    if (_persistentState.RecordExists && _persistentState.State is not null)
    {
        var restored = await _persistentState.State.ToMultiProjectionStateAsync(_domainTypes);
        if (restored.HasValue) _safeState = restored.Value;
        _logger.LogInformation("restored snapshot {SafeState}", _safeState?.ProjectorCommon.GetType().Name ?? "error");
    }

    // 2) catch‑up
    _logger.LogInformation("catch up from store");
    await CatchUpFromStoreAsync();

    // 4) snapshot timer
    _logger.LogInformation("start snapshot timer");
    _persistTimer = RegisterTimer(_ => PersistTick(), null, PersistInterval, PersistInterval);
    
    // ストリーム購読はInitStreamsAsync()に移動
}
```

6. 変更をテストして検証する
   - 異なるタイミングでグレインを起動させるユニットテストを作成
   - さまざまな負荷状況下でのシステム動作を検証

## 代替案（より簡易な修正）

より迅速な対応が必要な場合は、選択肢Bのように`OnActivateAsync`内のストリーム初期化コードをリトライロジックで囲むことも可能です：

```csharp
// 3) subscribe stream
_logger.LogInformation("subscribe stream");
const int maxAttempts = 6;
const int delayMs = 250;

for (var attempt = 0; attempt < maxAttempts; attempt++)
{
    try
    {
        _eventStream = this.GetStreamProvider("EventStreamProvider").GetStream<IEvent>(StreamId.Create("AllEvents", Guid.Empty));
        _subscription = await _eventStream.SubscribeAsync(OnStreamEventAsync, OnStreamErrorAsync, OnStreamCompletedAsync);
        
        _bootstrapping = false;
        _streamActive = true;
        _logger.LogInformation("stream active");
        FlushBuffer();
        _logger.LogInformation("stream buffer flushed");
        break;
    }
    catch (KeyNotFoundException) when (attempt < maxAttempts - 1)
    {
        _logger.LogWarning("Stream provider not available yet, retrying in {Delay}ms (attempt {Attempt}/{MaxAttempts})", 
                          delayMs * (attempt + 1), attempt + 1, maxAttempts);
        await Task.Delay(delayMs * (attempt + 1), ct);
    }
}
```

## まとめ

Orleans 9では、ストリーム購読のようなインフラストラクチャ依存のコードを`OnActivateAsync`から切り離し、グレインのライフサイクルステージを活用する方法が最も適切です。これにより、ストリームプロバイダーが準備できる前にアクセスしてしまう問題を効果的に解消できます。

本計画は、Orleans Streamsの仕組みとグレインのライフサイクルを理解した上で、最も安全で慣用的な解決策を提案しています。選択肢Aを優先的に検討し、必要に応じて選択肢Bを代替案として検討することを推奨します。
