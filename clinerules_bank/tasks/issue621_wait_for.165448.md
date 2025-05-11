# GitHub Copilot

## コマンド実行後のクエリ更新待機機能（WaitForSortableUniqueId）の改善実装計画

CQRSパターンを使用するSekibanでは、コマンド実行後に即座にクエリを実行すると、イベントがまだ完全に処理されておらず最新の状態が反映されない問題があります。`SortableUniqueIdValue`クラスの機能を活用した、より洗練された待機機能の実装計画を以下に示します。

### 1. 必要な変更点の概要

1. **WebコマンドAPIのレスポンス改善**
   - コマンド実行時にSortableUniqueIdを含むCommandResponseをそのままクライアントに返却する

2. **WebクライアントでのCommandResponse処理**
   - クライアントメソッドがCommandResponseを返すように修正し、SortableUniqueIdを活用できるようにする

3. **クエリインターフェースの拡張**
   - IWaitForSortableUniqueIdインターフェースを追加してクエリが特定のSortableUniqueIdを待機できるようにする

4. **Orleans実装の拡張**
   - MultiProjectorGrainに`SortableUniqueIdValue`クラスの比較機能を活用した待機機能を実装

### 2. 実装詳細

#### 2.1 WebコマンドAPIの修正

現状のAPIエンドポイントは`UnwrapBox()`を呼び出して内容を展開していますが、これではCommandResponseの重要な情報（SortableUniqueIdなど）が失われます。

**OrleansSekiban.ApiService/Program.cs** の修正:
- コマンドAPIからResultBox<CommandResponse>をそのまま返すように修正

```csharp
apiRoute
    .MapPost(
        "/inputweatherforecast",
        async (
            [FromBody] InputWeatherForecastCommand command,
            [FromServices] SekibanOrleansExecutor executor) => await executor.CommandAsync(command))
    .WithName("InputWeatherForecast")
    .WithOpenApi();
```

同様に他のコマンドAPI（RemoveWeatherForecast、UpdateWeatherForecastLocation）も修正します。

#### 2.2 WebクライアントAPIの修正

**OrleansSekiban.Web/WeatherApiClient.cs** の修正:
- 各コマンドメソッドがCommandResponseを返すように修正

```csharp
public async Task<CommandResponse> InputWeatherAsync(InputWeatherForecastCommand command, CancellationToken cancellationToken = default)
{
    var response = await httpClient.PostAsJsonAsync("/api/inputweatherforecast", command, cancellationToken);
    return await response.Content.ReadFromJsonAsync<CommandResponse>(cancellationToken) 
           ?? throw new InvalidOperationException("Failed to deserialize CommandResponse");
}
```

- GetWeatherAsyncメソッドを拡張して、オプショナルにWaitForSortableUniqueIdを受け取れるようにします

```csharp
public async Task<WeatherForecastQuery.WeatherForecastRecord[]> GetWeatherAsync(
    int maxItems = 10, 
    string? waitForSortableUniqueId = null,
    CancellationToken cancellationToken = default)
{
    // WaitForSortableUniqueIdを含むクエリを構築
    var query = new WeatherForecastQuery("") { WaitForSortableUniqueId = waitForSortableUniqueId };
    
    // 以下、既存の実装...
}
```

#### 2.3 クエリインターフェースの拡張

**新規インターフェース**: `IWaitForSortableUniqueId`

```csharp
namespace Sekiban.Pure.Query;

/// <summary>
/// Interface for queries that need to wait for a specific SortableUniqueId
/// </summary>
public interface IWaitForSortableUniqueId
{
    /// <summary>
    /// The SortableUniqueId to wait for before executing the query
    /// If null or empty, no waiting will be performed
    /// </summary>
    string? WaitForSortableUniqueId { get; set; }
}
```

このインターフェースは、クエリクラスに実装されることを想定しています。プロパティをget/setの両方にすることで、コードから動的に設定できるようにします。

#### 2.4 SortableUniqueIdの比較ユーティリティクラス

SortableUniqueIdValue機能をさらに活用するためのユーティリティクラスを作成します:

```csharp
namespace Sekiban.Pure.Documents;

/// <summary>
/// Utility class for SortableUniqueId operations related to waiting for events
/// </summary>
public static class SortableUniqueIdWaitHelper
{
    /// <summary>
    /// Default timeout for waiting for a SortableUniqueId (30 seconds)
    /// </summary>
    public const int DefaultWaitTimeoutMs = 30000;
    
    /// <summary>
    /// Default polling interval for checking if a SortableUniqueId has been processed (100ms)
    /// </summary>
    public const int DefaultPollingIntervalMs = 100;
    
    /// <summary>
    /// Determines if the target SortableUniqueId has been processed
    /// Uses SortableUniqueIdValue's built-in comparison methods
    /// </summary>
    /// <param name="currentId">The current (latest) SortableUniqueId that has been processed</param>
    /// <param name="targetId">The target SortableUniqueId we're waiting for</param>
    /// <returns>True if the target has been processed, false otherwise</returns>
    public static bool HasProcessedTargetId(string? currentId, string targetId)
    {
        if (string.IsNullOrEmpty(currentId))
        {
            return false;
        }
        
        var current = new SortableUniqueIdValue(currentId);
        var target = new SortableUniqueIdValue(targetId);
        
        // currentId >= targetId であれば処理済み
        return current.IsLaterThanOrEqual(target);
    }
    
    /// <summary>
    /// Calculates an appropriate waiting timeout based on the age of the SortableUniqueId
    /// Older events get shorter timeouts as they're likely already processed
    /// </summary>
    /// <param name="sortableUniqueId">The SortableUniqueId to analyze</param>
    /// <returns>Recommended timeout in milliseconds</returns>
    public static int CalculateAdaptiveTimeout(string sortableUniqueId)
    {
        var id = new SortableUniqueIdValue(sortableUniqueId);
        var eventTime = id.GetTicks();
        var age = DateTime.UtcNow - eventTime;
        
        // 5秒以上前のイベントは短いタイムアウト
        if (age.TotalSeconds > 5)
        {
            return Math.Min(5000, DefaultWaitTimeoutMs);
        }
        
        // 新しいイベントは標準のタイムアウト
        return DefaultWaitTimeoutMs;
    }
}
```

#### 2.5 IMultiProjectorGrainの拡張

**Sekiban.Pure.Orleans/IMultiProjectorGrain.cs** に以下のメソッドを追加:

```csharp
/// <summary>
/// Checks if the specified SortableUniqueId has been received and processed by this projector
/// </summary>
/// <param name="sortableUniqueId">The SortableUniqueId to check</param>
/// <returns>True if the SortableUniqueId has been received and processed, false otherwise</returns>
Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId);
```

#### 2.6 MultiProjectorGrainの実装

**Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs** に `IsSortableUniqueIdReceived` メソッドを実装:

```csharp
public Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId)
{
    // SortableUniqueIdValueクラスの比較機能を使用
    
    // バッファ内に存在するか確認
    if (_buffer.Any(e => new SortableUniqueIdValue(e.SortableUniqueId).IsLaterThanOrEqual(new SortableUniqueIdValue(sortableUniqueId))))
    {
        return Task.FromResult(true);
    }
    
    // LastSortableUniqueIdが目標のIDより新しいか確認
    if (!string.IsNullOrEmpty(_safeState?.LastSortableUniqueId))
    {
        var lastId = new SortableUniqueIdValue(_safeState.LastSortableUniqueId);
        var targetId = new SortableUniqueIdValue(sortableUniqueId);
        
        if (lastId.IsLaterThanOrEqual(targetId))
        {
            return Task.FromResult(true);
        }
    }
    
    return Task.FromResult(false);
}
```

#### 2.7 SekibanOrleansExecutorの拡張

**Sekiban.Pure.Orleans/Parts/SekibanOrleansExecutor.cs** のクエリメソッドを拡張して、IWaitForSortableUniqueIdをチェックして待機するロジックを追加:

```csharp
private async Task WaitForSortableUniqueIdIfNeeded(IMultiProjectorGrain multiProjectorGrain, object query)
{
    if (query is IWaitForSortableUniqueId waitForQuery && 
        !string.IsNullOrEmpty(waitForQuery.WaitForSortableUniqueId))
    {
        var sortableUniqueId = waitForQuery.WaitForSortableUniqueId;
        
        // SortableUniqueIdの年齢に基づいて待機時間を適応的に調整
        var timeoutMs = SortableUniqueIdWaitHelper.CalculateAdaptiveTimeout(sortableUniqueId);
        var pollingIntervalMs = SortableUniqueIdWaitHelper.DefaultPollingIntervalMs;
        
        var stopwatch = Stopwatch.StartNew();
        
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            // 指定されたSortableUniqueIdが処理されたかチェック
            var isReceived = await multiProjectorGrain.IsSortableUniqueIdReceived(sortableUniqueId);
            if (isReceived)
            {
                return; // 処理済みなら待機終了
            }
            
            // 一定時間待機
            await Task.Delay(pollingIntervalMs);
        }
        
        // タイムアウト時のログ記録
        // ただし処理は続行する（ベストエフォート）
        Logger.LogWarning($"Timeout waiting for SortableUniqueId {sortableUniqueId} after {timeoutMs}ms");
    }
}
```

これをQueryAsyncメソッドに組み込みます:

```csharp
public async Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull
{
    var projectorResult = sekibanDomainTypes.QueryTypes.GetMultiProjector(queryCommon);
    if (!projectorResult.IsSuccess)
        return ResultBox<TResult>.Error(new ApplicationException("Projector not found"));
    var nameResult
        = sekibanDomainTypes.MultiProjectorsType
            .GetMultiProjectorNameFromMultiProjector(projectorResult.GetValue());
    if (!nameResult.IsSuccess)
        return ResultBox<TResult>.Error(new ApplicationException("Projector name not found"));
    var multiProjectorGrain = clusterClient.GetGrain<IMultiProjectorGrain>(nameResult.GetValue());
    
    // 待機ロジックを追加
    await WaitForSortableUniqueIdIfNeeded(multiProjectorGrain, queryCommon);
    
    var result = await multiProjectorGrain.QueryAsync(queryCommon);
    return result.ToResultBox().Remap(a => a.GetValue()).Cast<TResult>();
}
```

ListQueryCommon用のメソッドも同様に修正します。

### 3. 使用例

実装後、以下のように使用できるようになります:

```csharp
// 例1: コマンド実行後すぐにクエリを実行する場合
var commandResponse = await weatherApiClient.InputWeatherAsync(new InputWeatherForecastCommand(...));
var result = await weatherApiClient.GetWeatherAsync(maxItems: 10, waitForSortableUniqueId: commandResponse.SortableUniqueId);

// 例2: クエリオブジェクトを直接使用する場合
var query = new WeatherForecastQuery("") { WaitForSortableUniqueId = commandResponse.SortableUniqueId };
var result = await executor.QueryAsync(query);
```

### 4. 実装の注意点とパフォーマンス最適化

1. **SortableUniqueIdValueの活用**:
   - 文字列比較ではなく、SortableUniqueIdValueクラスの比較メソッドを使用することで、意図が明確になる
   - SortableUniqueIdValueが持つ日時情報を活用して、待機時間を動的に最適化

2. **待機時間の適応的調整**:
   - SortableUniqueIdから抽出した時間情報に基づいて待機時間を調整
   - 古いイベント（既に処理されている可能性が高い）は短い待機時間、新しいイベントは標準の待機時間

3. **安全マージンの考慮**:
   - SortableUniqueIdValue.SafeMilliseconds (5000ms) を参考に、適切なタイムアウト値を設定
   - デフォルトタイムアウトは30秒程度（SafeMillisecondsの6倍）に設定し、処理の遅延にも対応

4. **設定の柔軟性**:
   - タイムアウトや待機間隔を設定ファイルから読み取れるようにし、環境に応じて調整可能に

### 5. 将来の拡張ポイント

1. **待機のキャンセル機能**:
   - CancellationTokenを追加し、クライアントが待機をキャンセルできるようにする

2. **進行状況の通知**:
   - 長い待機が発生した場合に、進行状況をログやイベントで通知する仕組み

3. **分散環境での最適化**:
   - 複数ノードにまたがる場合の待機状態の共有や調整メカニズム

4. **待機ポリシーの拡張**:
   - 「特定の時間まで待機」「特定のイベント数まで待機」など、より複雑な待機条件の実装

5. **パフォーマンス監視**:
   - 待機が頻繁に発生する場合や長時間の待機が発生する場合に、システム全体のパフォーマンスを監視し最適化するための指標収集
