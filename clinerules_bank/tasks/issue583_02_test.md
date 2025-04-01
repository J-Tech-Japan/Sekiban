clinerules_bank/tasks/issue583_01_restore.md

で書いた機能のテストを記述したい

tests/Pure.Domain.xUnit

ないにテストを記述してください。

src/Sekiban.Pure.Orleans/Parts/SerializableMultiProjectionState.cs

に記述して保存してリストアがうまくいくか

internalUsages/Pure.Domain/MultiProjectorPayload.cs

を使うことができるのと、

src/Sekiban.Pure/Projectors/AggregateListProjector.cs
を使って AggregateListProjector<BranchProjector> 

などもテストできる

以上が行いたいことですが、設計を考えて、どのような変更を行うかの方針を具体的にこのファイルに追記してください。
clinerules_bank/tasks/issue583_02_test.md

わからないことは質問してその答えを得てから進めてください。わからないことを勝手に進めても成功しません。

実装方針を読んだら一旦終了してください。レビューをしてよければ、実装方針の通り進めるようにこちらから依頼します。

この行の上は削除しないでください。
ーーーーーーーーーーーーーーーーーーーーーーーー以下の行に追記可能-------------------

## SerializableMultiProjectionState テスト実装方針

SerializableMultiProjectionState の機能を検証するための包括的なテスト方針を以下に示します。このテスト実装は、Orleans での状態の保存とリストア機能が正常に動作することを確認するために設計されています。

### 1. テストクラスの設計

```csharp
namespace Pure.Domain.xUnit;

/// <summary>
/// SerializableMultiProjectionState のシリアライズ/デシリアライズをテストするためのテストクラス
/// </summary>
public class SerializableMultiProjectionStateTests
{
    // テストメソッドを実装
}
```

### 2. テスト対象の機能

テストでは以下の内容を検証します：

1. **基本的なシリアライズ/デシリアライズテスト**:
   - `MultiProjectorPayload` を使用して基本的な変換が正常に動作することを確認
   - シリアライズから正確なデシリアライズが行われることを確認

2. **`AggregateListProjector<BranchProjector>` 変換テスト**:
   - `AggregateListProjector<BranchProjector>` インスタンスで変換が正常に動作することを確認
   - アグリゲートを含む状態を正確に保存/復元できることを確認

3. **バージョン互換性テスト**:
   - 異なるバージョン値での動作確認
   - バージョン非互換時の適切なエラー処理（`OptionalValue.None` の返却）

4. **型互換性テスト**:
   - 型名の不一致時の適切なエラー処理を確認
   - 間違った型変換試行時の挙動確認

5. **エッジケーステスト**:
   - `null` 値を含むケース
   - 大きなデータセットでの圧縮/解凍の検証
   - 破損データのハンドリング

### 3. テスト実装の詳細

#### 3.1 テストメソッド1: 基本的なシリアライズ/デシリアライズテスト

```csharp
[Fact]
public async Task SerializeDeserialize_MultiProjectorPayload_Success()
{
    // Arrange
    // - 初期の MultiProjectorPayload インスタンスを作成
    // - テスト用にいくつかのユーザーとカートを追加
    // - MultiProjectionState を構築

    // Act
    // - MultiProjectionState → SerializableMultiProjectionState に変換
    // - SerializableMultiProjectionState → MultiProjectionState に変換

    // Assert
    // - 元の状態と復元された状態を比較
    // - ユーザーデータとカートデータが完全に一致することを検証
}
```

#### 3.2 テストメソッド2: AggregateListProjector テスト

```csharp
[Fact]
public async Task SerializeDeserialize_AggregateListProjector_Success()
{
    // Arrange
    // - AggregateListProjector<BranchProjector> インスタンスを作成
    // - テスト用に複数のブランチエンティティを追加
    // - MultiProjectionState を構築

    // Act
    // - MultiProjectionState → SerializableMultiProjectionState に変換
    // - SerializableMultiProjectionState → MultiProjectionState に変換

    // Assert
    // - 元の状態と復元された状態を比較
    // - すべてのブランチデータが正確に保持されていることを確認
    // - 各ブランチのプロパティが正確に復元されていることを確認
}
```

#### 3.3 テストメソッド3: バージョン不一致テスト

```csharp
[Fact]
public async Task Deserialize_VersionMismatch_ReturnsNone()
{
    // Arrange
    // - 標準のMultiProjectionStateを作成
    // - SerializableMultiProjectionStateに変換
    // - バージョン値を手動で変更

    // Act
    // - 変更されたバージョンのSerializableMultiProjectionStateからMultiProjectionStateへの変換を試行

    // Assert
    // - 結果が OptionalValue.None であることを確認
}
```

#### 3.4 テストメソッド4: 型不一致テスト

```csharp
[Fact]
public async Task Deserialize_TypeNameMismatch_ReturnsNone()
{
    // Arrange
    // - MultiProjectorPayload の MultiProjectionState を作成
    // - SerializableMultiProjectionState に変換
    // - PayloadTypeName を手動で変更

    // Act
    // - 変更された型名のSerializableMultiProjectionStateからMultiProjectionStateへの変換を試行

    // Assert
    // - 結果が OptionalValue.None であることを確認
}
```

#### 3.5 テストメソッド5: 大規模データテスト

```csharp
[Fact]
public async Task SerializeDeserialize_LargeData_Success()
{
    // Arrange
    // - 多数のエンティティを含む大きなMultiProjectorPayloadを作成
    // - MultiProjectionStateを構築

    // Act
    // - MultiProjectionState → SerializableMultiProjectionState に変換
    // - SerializableMultiProjectionState → MultiProjectionState に変換

    // Assert
    // - 変換前後でデータが一致することを確認
    // - 特に大量データの圧縮と解凍が正確に行われることを検証
}
```

### 4. 実装に必要なヘルパー

テスト実装で必要となる主なヘルパーメソッドとクラス：

1. **JsonSerializerOptions モック**:
   - テストで使用するJsonSerializerOptionsを作成するメソッド
   - 実環境と同様の設定を適用するか、テスト用に最適化した設定を使用

2. **データ比較ヘルパー**:
   - MultiProjectorPayloadのインスタンス同士を比較するためのヘルパーメソッド
   - 深いプロパティレベルでの比較ロジック 

3. **テストデータジェネレーター**:
   - テスト用の様々なデータセットを生成するメソッド
   - 小規模/大規模のデータ、特殊ケースデータなど

### 5. テストにおける重要な考慮事項

1. **非同期コードの適切なテスト**:
   - すべての非同期オペレーション（圧縮/解凍など）が正しくテストされることを確認

2. **エラーケースの包括的な検証**:
   - 型の不一致、バージョンの不一致、無効なデータなど様々なエラーケースの検証

3. **Orleans依存の分離**:
   - テストがOrleans Silosに依存せずに実行できるよう、SerializableMultiProjectionStateの機能を独立してテスト

4. **実際のシナリオのシミュレーション**:
   - 実際のアプリケーションの使用パターンに基づいたテストを含める
   - 例：ドメインイベントの処理とその後の状態の保存/復元

### 6. 具体的なテスト実装例

Pure.Domain.xUnitプロジェクトに新しいテストファイル `SerializableMultiProjectionStateTests.cs` を作成し、上記のテストメソッドを実装します。具体的な実装では、SerializableMultiProjectionStateクラスの変換メソッドへの入力として有効なMultiProjectionStateオブジェクトを構築し、変換と逆変換の前後で状態が正確に保持されることを検証します。

各テストは、異なるプロジェクターの型（MultiProjectorPayload、AggregateListProjector<BranchProjector>など）に対して行い、シリアライズ/デシリアライズのプロセスが正しく機能することを確認します。また、エラーケース（バージョン不一致、型不一致など）も適切に処理されることを検証します。

このテスト実装により、SerializableMultiProjectionStateクラスが期待通りに動作し、Orleans環境でのグレイン状態の保存と復元が正しく行われることが確認できます。
