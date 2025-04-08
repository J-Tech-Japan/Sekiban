clinerules_bank/tasks/issue583_01_restore.md

と似たことですが、

src/Sekiban.Pure.Orleans/Grains/AggregateProjectorGrain.cs

でも行う必要があります。

Blobに保存して、復旧できるようにするためのクラスを書いて、呼ぶための設計をしてください。

以上が行いたいことですが、設計を考えて、どのような変更を行うかの方針を具体的にこのファイルに追記してください。

clinerules_bank/tasks/issue583_03_single.md

わからないことは質問してその答えを得てから進めてください。わからないことを勝手に進めても成功しません。

実装方針を書き込んだら一旦終了してください。レビューをしてよければ、実装方針の通り進めるようにこちらから依頼します。

この行の上は削除しないでください。
ーーーーーーーーーーーーーーーーーーーーーーーー以下の行に追記可能-------------------

## 実装方針案: AggregateProjectorGrain のシリアライズ可能な状態クラスの設計

### 問題概要
Orleans の GrainState 永続化において、`AggregateProjectorGrain` の状態として使用される `Aggregate` クラスには `IAggregatePayload Payload` というインターフェース型のプロパティが含まれており、`System.Text.Json` がこれを直接シリアライズできないために、Blob ストレージからの復元時にエラーが発生しています。

### 解決策の概要
`MultiProjectorGrain` で実装した方法と同様に、シリアライズ可能な形式で状態を保持する新しいクラス `SerializableAggregate` を作成し、`AggregateProjectorGrain` がこれを使用するように変更します。

### 実装詳細

1. **`SerializableAggregate` クラスの作成:**

   新しいファイル `src/Sekiban.Pure.Orleans/Parts/SerializableAggregate.cs` を作成し、以下の内容を実装します：
   - シリアライズ可能なプロパティのみを持つレコード型として定義
   - `IAggregatePayload` インターフェースのインスタンスをJSON形式でシリアライズし、GZipで圧縮して保持
   - バージョン互換性チェックのために必要なメタデータを保持
   - Aggregate ⇔ SerializableAggregate 間の変換メソッドを提供

2. **主要なプロパティ:**
   ```csharp
   // 元のAggregateから直接コピーする値
   public PartitionKeys PartitionKeys { get; set; }
   public int Version { get; set; }
   public string LastSortableUniqueId { get; set; }
   public string ProjectorVersion { get; set; }
   public string ProjectorTypeName { get; set; }
   public string PayloadTypeName { get; set; }
   
   // IAggregatePayloadをシリアライズして圧縮したデータ
   public byte[] CompressedPayloadJson { get; set; }
   
   // バージョン互換性チェック用
   public string PayloadAssemblyVersion { get; set; }
   ```

3. **主要な変換メソッド:**
   ```csharp
   // Aggregate → SerializableAggregate
   public static async Task<SerializableAggregate> CreateFromAsync(
       Aggregate aggregate, JsonSerializerOptions options);
       
   // SerializableAggregate → Aggregate
   public async Task<OptionalValue<Aggregate>> ToAggregateAsync(
       IAggregateProjector projector, JsonSerializerOptions options);
   ```

4. **`IAggregateProjector` インターフェースの拡張:**

   `src/Sekiban.Pure/Projectors/IAggregateProjector.cs` に型名からペイロードの型を取得するためのメソッドを追加します：
   ```csharp
   Type? GetPayloadTypeByName(string payloadTypeName);
   ```
   
   基底実装クラスにはデフォルト実装を提供：
   ```csharp
   public virtual Type? GetPayloadTypeByName(string payloadTypeName)
   {
       // 実装クラスのアセンブリ内で型名を検索
       return GetType().Assembly.GetTypes()
           .FirstOrDefault(t => t.Name == payloadTypeName && 
                               typeof(IAggregatePayload).IsAssignableFrom(t));
   }
   ```

5. **`AggregateProjectorGrain` の変更:**

   - Grain の状態型を変更：
     ```csharp
     [PersistentState("aggregate", "Default")] IPersistentState<SerializableAggregate> state
     ```
   
   - 現在のAggregate状態をメモリ内に保持するフィールドを追加：
     ```csharp
     private Aggregate _currentAggregate = Aggregate.Empty;
     ```
   
   - JSON シリアライズオプション用のプロパティを追加：
     ```csharp
     private JsonSerializerOptions JsonOptions => sekibanDomainTypes.JsonSerializerOptions;
     ```
   
   - `OnActivateAsync` メソッドを更新して SerializableAggregate から Aggregate への変換を行う
   
   - 同様に他のメソッドも更新して、シリアライズ可能な状態との相互変換を行う
   
   - 状態保存用のヘルパーメソッドを追加：
     ```csharp
     private async Task WriteSerializableStateAsync()
     {
         state.State = await SerializableAggregate.CreateFromAsync(_currentAggregate, JsonOptions);
         await state.WriteStateAsync();
     }
     ```

6. **エラー処理とバージョン互換性:**

   - デシリアライズに失敗した場合は `OptionalValue<Aggregate>.Empty` を返し、呼び出し側で再構築を行う
   - 型の互換性チェックとプロジェクターバージョンチェックを実装して堅牢性を確保
   - ログ出力を追加して問題診断をサポート

### 実装の利点

1. **シリアライズの安全性:** インターフェース型の直接シリアライズを避け、具体的なデータ形式で保存することで Orleans の永続化の問題を解決

2. **バージョン互換性:** アプリケーションやモデル構造の変更に対して堅牢な設計となり、バージョン間の互換性チェックを実装

3. **データ効率:** GZip圧縮により、保存データサイズを削減

4. **フォールバックメカニズム:** デシリアライズに問題が発生した場合、自動的にイベントストリームから再構築するフォールバックメカニズムを備える

### 実装時の注意点

1. **シリアライズ設定の一貫性:** アプリケーション全体で同じJSONシリアライズ設定を使用する必要がある

2. **パフォーマンス:** 圧縮・解凍操作は CPU 負荷が高いため、必要に応じてパフォーマンスチューニングを検討

3. **エラーハンドリング:** 変換エラーを適切に捕捉し、再構築ロジックへのフォールバックを確実に行う

4. **ユニットテスト:** シリアライズ・デシリアライズの互換性テストを追加し、異なるバージョン間の変換をテスト

この実装により、`AggregateProjectorGrain` も MultiProjectorGrain と同様に、Blob ストレージへの永続化と復元が正常に機能するようになります。
