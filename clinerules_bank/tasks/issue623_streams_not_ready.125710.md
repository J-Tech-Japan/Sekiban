LLM Model: (モデル名が不明なため、後ほど追記します)

# issue623_streams_not_ready の対応計画

## 問題点

`src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs` の `OnActivateAsync` メソッド内で、Orleans Stream を取得する際に、ストリームの準備ができていないことが原因で `GetStreamProvider("EventStreamProvider")` が例外をスローする可能性がある。

## 提案される解決策

ストリームプロバイダーの取得とストリームへのサブスクライブ処理をリトライ可能にし、Orleans の起動シーケンスやストリームプロバイダーの準備が完了するまで待機するように変更する。

### 具体的なステップ

1.  **リトライロジックの実装**:
    *   `GetStreamProvider` および `SubscribeAsync` の呼び出しを `try-catch` ブロックで囲む。
    *   例外が発生した場合、一定時間待機してからリトライする。
    *   リトライ回数に上限を設け、上限に達した場合はエラーログを出力して処理を中断するか、Grain のアクティベーションを失敗させる。
    *   待機時間には指数バックオフのような戦略を検討する。

2.  **Orleans の Stream Lifecycle の確認**:
    *   Orleans のドキュメントや関連情報を調査し、Stream Provider が利用可能になるタイミングや、それを確認するための推奨される方法があるかを確認する。
    *   `IStreamProviderManager` などを利用して、Stream Provider の状態を確認できるか調査する。

3.  **設定値の導入**:
    *   リトライ回数や待機時間などのパラメータを設定ファイルや Grain のコンストラクタ経由で設定可能にすることを検討する。これにより、環境に応じた調整が容易になる。

4.  **ログ出力の強化**:
    *   リトライ処理の開始、リトライ試行、成功、失敗などの各ステップで詳細なログを出力するようにする。これにより、問題発生時の原因究明が容易になる。

5.  **テスト**:
    *   Stream Provider が一時的に利用できない状況をシミュレートするテストケースを作成し、リトライロジックが正しく機能することを確認する。

### コード変更のイメージ (src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs)

```csharp
// OnActivateAsync メソッド内

// ... (既存のコード) ...

// 3) subscribe stream
_logger.LogInformation("subscribe stream");
const int maxRetries = 5; // 設定可能にすることを検討
const int delayMilliseconds = 1000; // 設定可能にすることを検討

for (int i = 0; i < maxRetries; i++)
{
    try
    {
        _eventStream = this.GetStreamProvider("EventStreamProvider").GetStream<IEvent>(StreamId.Create("AllEvents", Guid.Empty));
        _subscription = await _eventStream.SubscribeAsync(OnStreamEventAsync, OnStreamErrorAsync, OnStreamCompletedAsync);
        _logger.LogInformation("Successfully subscribed to stream after {Attempts} attempt(s).", i + 1);
        break; // 成功したらループを抜ける
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to subscribe to stream on attempt {AttemptNumber}. Retrying in {DelayMilliseconds}ms...", i + 1, delayMilliseconds);
        if (i < maxRetries - 1)
        {
            await Task.Delay(delayMilliseconds * (i + 1)); // 指数バックオフ的な待機
        }
        else
        {
            _logger.LogError(ex, "Failed to subscribe to stream after {MaxRetries} attempts. Activation failed.", maxRetries);
            // ここで Grain のアクティベーションを失敗させるか、エラー状態としてマークするなどの処理を行う
            // 例: throw new OrleansException("Failed to subscribe to stream after multiple retries.", ex);
            // または、_streamActive = false; のようなフラグ管理で対応
            _streamActive = false; // ストリームがアクティブでないことを示す
            // 必要に応じて、Grain の Deactivate を検討
            // DeactivateOnIdle(); // もしくは適切なエラーハンドリング
            return; // アクティベーション処理を中断
        }
    }
}

// ... (既存のコード) ...
```

### 検討事項

*   `DeactivateOnIdle()` を呼び出して Grain を非アクティブ化するか、あるいはエラー状態を示すフラグを立てて、Grain の他の操作が失敗するようにするか。
*   リトライ処理中に `CancellationToken` を尊重するかどうか。`OnActivateAsync` の `ct` を `Task.Delay` に渡すなど。
*   Orleans のベストプラクティスとして、このような初期化時の依存関係の待機処理がどのように行われるべきか、より詳細な調査を行う。例えば、Silo の起動時に Stream Provider が完全に初期化されるのを待つ仕組みがあるかなど。

## 次のステップ

1.  Orleans の Stream Lifecycle に関するドキュメントを再確認する。
2.  上記計画に基づいて、`MultiProjectorGrain.cs` の改修案を具体的にコードに落とし込む。
3.  テスト方法を検討する。
