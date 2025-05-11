# GitHub Copilot

## コマンド実行後のクエリ更新待機機能（WaitForSortableUniqueId）の実装計画

CQRSパターンを使用するSekibanでは、コマンド実行後に即座にクエリを実行すると、イベントが完全に処理されていないために最新の状態が反映されていない問題があります。この問題を解決するための実装計画を以下に示します。

### 1. 必要な変更点の概要

1. **WebコマンドAPIのレスポンス改善**
   - コマンド実行時にSortableUniqueIdを含むCommandResponseをクライアントに返却する

2. **WebクライアントでのCommandResponse処理**
   - クライアントメソッドがCommandResponseを取得して利用できるようにする

3. **クエリインターフェースの拡張**
   - IWaitForSortableUniqueIdインターフェースを追加してクエリが特定のSortableUniqueIdを待機できるようにする

4. **Orleans実装の拡張**
   - MultiProjectorGrainに待機機能を追加し、SekibanOrleansExecutorで待機ロジックを実装

### 2. 実装詳細

#### 2.1 WebコマンドAPIの修正

現状のAPIエンドポイントは `UnwrapBox()`を呼び出していますが、これではCommandResponseの重要な情報（SortableUniqueIdなど）が失われる可能性があります。

**OrleansSekiban.ApiService/Program.cs** の修正:
- コマンドハンドラーで返されるCommandResponseをそのまま返すように修正

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

同様に他のメソッド（RemoveWeatherAsync、UpdateLocationAsync）も修正します。

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
    string? WaitForSortableUniqueId { get; }
}
```

このインターフェースは、クエリクラスに実装されることを想定しています。

#### 2.4 IMultiProjectorGrainの拡張

**Sekiban.Pure.Orleans/IMultiProjectorGrain.cs** に以下のメソッドを追加:

```csharp
/// <summary>
/// Checks if the specified SortableUniqueId has been received and processed by this projector
/// </summary>
/// <param name="sortableUniqueId">The SortableUniqueId to check</param>
/// <returns>True if the SortableUniqueId has been received and processed, false otherwise</returns>
Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId);
```

#### 2.5 MultiProjectorGrainの実装

**Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs** に `IsSortableUniqueIdReceived` メソッドを実装:

```csharp
public Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId)
{
    // すでにバッファにあるか確認
    if (_buffer.Any(e => string.Compare(e.SortableUniqueId, sortableUniqueId, StringComparison.Ordinal) >= 0))
    {
        return Task.FromResult(true);
    }
    
    // すでに処理済みのイベントか確認
    if (_safeState?.LastSortableUniqueId != null && 
        string.Compare(_safeState.LastSortableUniqueId, sortableUniqueId, StringComparison.Ordinal) >= 0)
    {
        return Task.FromResult(true);
    }
    
    return Task.FromResult(false);
}
```

#### 2.6 SekibanOrleansExecutorの拡張

**Sekiban.Pure.Orleans/Parts/SekibanOrleansExecutor.cs** のクエリメソッドを拡張して、IWaitForSortableUniqueIdをチェックして待機するロジックを追加:

```csharp
private async Task WaitForSortableUniqueIdIfNeeded(IMultiProjectorGrain multiProjectorGrain, object query)
{
    if (query is IWaitForSortableUniqueId waitForQuery && 
        !string.IsNullOrEmpty(waitForQuery.WaitForSortableUniqueId))
    {
        // 最大待機時間とポーリング間隔の設定（設定から取得するか、デフォルト値を使用）
        var maxWaitTimeMs = 30000; // 30秒 (設定から取得可能)
        var pollingIntervalMs = 100; // 100ミリ秒 (設定から取得可能)
        
        var stopwatch = Stopwatch.StartNew();
        var sortableUniqueId = waitForQuery.WaitForSortableUniqueId;
        
        while (stopwatch.ElapsedMilliseconds < maxWaitTimeMs)
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
        
        // タイムアウトした場合でも続行（ベストエフォート）
    }
}
```

このメソッドを `QueryAsync<TResult>` と `QueryAsync<TResult>` (ListQuery用) の両方で呼び出すように修正:

```csharp
public async Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull
{
    // 既存のコード...
    var multiProjectorGrain = clusterClient.GetGrain<IMultiProjectorGrain>(nameResult.GetValue());
    
    // 待機ロジックを追加
    await WaitForSortableUniqueIdIfNeeded(multiProjectorGrain, queryCommon);
    
    var result = await multiProjectorGrain.QueryAsync(queryCommon);
    return result.ToResultBox().Remap(a => a.GetValue()).Cast<TResult>();
}
```

同様に `QueryAsync<TResult>` (ListQuery用) も修正します。

### 3. 使用例

実装後、以下のように使用できるようになります:

```csharp
// コマンド実行してSortableUniqueIdを取得
var commandResponse = await weatherApiClient.InputWeatherAsync(new InputWeatherForecastCommand(...));
string sortableUniqueId = commandResponse.SortableUniqueId;

// クエリで待機を指定
var query = new WeatherForecastQuery("") { WaitForSortableUniqueId = sortableUniqueId };
var result = await weatherApiClient.GetWeatherAsync(query);
```

### 4. 実装の注意点

1. **パフォーマンスの考慮**:
   - 待機時間やポーリング間隔は設定から取得可能にし、システム要件に合わせて調整できるようにする
   - 永久待機を避けるため、必ずタイムアウト設定を入れる

2. **エラーハンドリング**:
   - タイムアウト時のエラー処理オプションを検討（例：例外をスロー、警告ログ、ベストエフォートで続行）

3. **テスト**:
   - 単体テストと統合テストを書き、待機機能が正しく動作することを確認

### 5. 将来の拡張ポイント

1. **待機の進捗通知**:
   - クライアントに待機の進捗を通知する仕組み（WebSocketなど）

2. **クラスタ環境での最適化**:
   - 複数のノードにまたがる待機処理の最適化

3. **待機のキャンセル機能**:
   - CancellationTokenを使用した待機のキャンセル機能
