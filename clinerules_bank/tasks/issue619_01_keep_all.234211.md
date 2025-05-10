# Model: GitHub Copilot

## MultiProjectorGrainのストリーム処理改善計画

### 現状分析

現在の`MultiProjectorGrain.cs`では以下のような問題があります：

1. ストリームイベント処理（`OnStreamEventAsync`）：
   - `_bootstrapping`が`true`の場合のみバッファ（`_buffer`）にイベントを追加
   - `_bootstrapping`が`false`の場合は直接`ApplyEventAsync`を呼び出してしまう
   
2. バッファ処理（`FlushBuffer`）：
   - 単純にバッファからイベントを取り出して`ApplyEventAsync`を呼ぶだけ
   - SortableUniqueIdによるソートや、古いイベントと新しいイベントの分離がない
   
3. 状態管理：
   - `_safeState`：安定した状態（スナップショットとして保存される）
   - `_unsafeState`：最新の不安定な状態（最近のイベントを適用した状態）
   - `UpdateProjectionAsync`で時間（`SafeStateWindow`=7秒）に基づいて分離

### 修正方針

要件に基づく修正内容は以下の通りです：

1. ストリームイベント処理の変更：
   - `_bootstrapping`フラグに関わらず、常にイベントをバッファに追加するだけにする
   
2. バッファ処理（`FlushBuffer`）の改善：
   - バッファのイベントをSortableUniqueIdでソート
   - SafeStateWindowに基づいて古いイベントと新しいイベントを分離
   - 古いイベントは`_safeState`に適用し、バッファから削除
   - 残りのイベントは`_unsafeState`を生成するのに使用
   
3. バッファの実装変更：
   - 現在の`Queue<IEvent>`から、ソートと削除が容易な`List<IEvent>`に変更

### 具体的な修正計画

#### 1. バッファの実装変更

```csharp
// 変更前
private readonly Queue<IEvent> _buffer = new();

// 変更後
private readonly List<IEvent> _buffer = new();
```

#### 2. ストリームイベント処理の変更

```csharp
// 変更前
private Task OnStreamEventAsync(IEvent e, StreamSequenceToken? _) =>
    _bootstrapping ? Enqueue(e) : ApplyEventAsync(e);

// 変更後
private Task OnStreamEventAsync(IEvent e, StreamSequenceToken? _) => Enqueue(e);
```

#### 3. Enqueueメソッドの変更

```csharp
// 変更前
private Task Enqueue(IEvent e)
{
    _buffer.Enqueue(e);
    return Task.CompletedTask;
}

// 変更後
private Task Enqueue(IEvent e)
{
    _buffer.Add(e);
    return Task.CompletedTask;
}
```

#### 4. FlushBufferメソッドの全面的な書き換え

```csharp
private void FlushBuffer()
{
    // バッファが空なら何もしない
    if (!_buffer.Any()) return;
    
    // プロジェクターを取得
    var projector = GetProjectorFromName();
    
    // バッファをSortableUniqueIdでソート
    _buffer.Sort((a, b) => 
        new SortableUniqueIdValue(a.SortableUniqueId).CompareTo(
            new SortableUniqueIdValue(b.SortableUniqueId)));
    
    // safeBorderを計算
    var safeBorder = new SortableUniqueIdValue((DateTime.UtcNow - SafeStateWindow).ToString("O"));
    
    // safeBorderより古いイベントのインデックスを探す
    int splitIndex = _buffer.FindLastIndex(e => 
        new SortableUniqueIdValue(e.SortableUniqueId).IsEarlierThan(safeBorder));
    
    // 古いイベントがあれば処理
    if (splitIndex >= 0)
    {
        // 古いイベントを取得
        var oldEvents = _buffer.Take(splitIndex + 1).ToList();
        
        // _safeStateに適用
        var newSafeState = _domainTypes.MultiProjectorsType.Project(projector, oldEvents).UnwrapBox();
        var lastOldEvt = oldEvents.Last();
        _safeState = new MultiProjectionState(
            newSafeState, 
            lastOldEvt.Id, 
            lastOldEvt.SortableUniqueId,
            (_safeState?.Version ?? 0) + 1, 
            0, 
            _safeState?.RootPartitionKey ?? "default");
        
        // 適用したイベントはバッファから削除
        _buffer.RemoveRange(0, splitIndex + 1);
        
        // スナップショット更新フラグをセット
        _pendingSave = true;
    }
    
    // バッファに残っているイベント（新しいイベント）があり、かつ_safeStateが初期化されていれば
    if (_buffer.Any() && _safeState != null)
    {
        // _unsafeStateを更新
        var newUnsafeState = _domainTypes.MultiProjectorsType.Project(projector, _buffer).UnwrapBox();
        var lastNewEvt = _buffer.Last();
        _unsafeState = new MultiProjectionState(
            newUnsafeState,
            lastNewEvt.Id,
            lastNewEvt.SortableUniqueId,
            _safeState.Version + 1,
            0,
            _safeState.RootPartitionKey);
    }
    // 注意: バッファの残りのイベントは削除せず、保持しておく
}
```

#### 5. BuildStateIfNeededAsyncメソッドの微調整

現状のメソッドはそのまま使用できますが、処理の流れが変わることを確認するためにコメントを追加します。

```csharp
private async Task<MultiProjectionState> BuildStateIfNeededAsync()
{
    if (_safeState is null)
    {
        if (_streamActive)
        {
            // ストリームアクティブの場合はバッファを処理
            // 新しいFlushBuffer実装により、古いイベントは_safeStateに適用され、
            // 新しいイベントは_unsafeStateに適用される
            FlushBuffer();
        }
        else
        {
            // ストリームが非アクティブの場合はイベントストアから読み込む
            await CatchUpFromStoreAsync();
        }
    }
    return _unsafeState ?? _safeState ?? throw new InvalidOperationException("State not initialized");
}
```

### 注意点と考慮事項

1. **バッファの管理**：  
   バッファから古いイベントだけを削除し、新しいイベントは保持しておくことで、次回のFlushBufferで再評価できるようにします。

2. **ソートの効率**：  
   多数のイベントがバッファにある場合のソート処理が効率的であることを確認します。

3. **状態の一貫性**：  
   `_safeState`と`_unsafeState`の関係性を保ちながら、イベントが適切に処理されることを確認します。

4. **SortableUniqueIdValueの比較**：  
   `IsEarlierThan`メソッドを使用して時間的な前後関係を正確に判定します。

5. **例外処理**：  
   イベント処理中に例外が発生した場合の回復メカニズムを検討します。

6. **パフォーマンスへの影響**：  
   バッファのサイズが大きくなった場合のメモリ使用量と処理時間を考慮します。

### テスト戦略

1. **単体テスト**：
   - イベントをバッファに追加する処理のテスト
   - FlushBufferでのソートと分割処理のテスト
   - _safeStateと_unsafeStateの更新ロジックのテスト

2. **統合テスト**：
   - ストリームからイベントを受信してからクエリ実行までの一連の流れのテスト
   - 時間経過に伴う_safeStateと_unsafeStateの変化のテスト

3. **エッジケース**：
   - バッファが空の場合の処理
   - 全てのイベントがsafeBorderより古い場合
   - 全てのイベントがsafeBorderより新しい場合
   - _safeStateがnullの状態での処理

この計画により、要件に沿ったストリーム処理の改善を実現できると考えます。
