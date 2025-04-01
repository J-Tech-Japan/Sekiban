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
## テスト実装方針

1.  **テスト対象の分析:**
    *   `src/Sekiban.Pure.Orleans/Parts/SerializableMultiProjectionState.cs`: Orleans環境でのMultiProjectionの状態をシリアライズ可能にするクラス。
    *   `internalUsages/Pure.Domain/MultiProjectorPayload.cs`: 複数のプロジェクターペイロードを保持できるカスタムペイロード。
    *   `src/Sekiban.Pure/Projectors/AggregateListProjector.cs`: 特定のAggregateProjectorに対応するAggregateのリストを保持する汎用プロジェクター。
    *   `clinerules_bank/tasks/issue583_01_restore.md` で実装された保存・リストア機能（具体的な実装内容は不明なため、Orleansの標準的な永続化機構を利用していると仮定）。

2.  **テストシナリオの定義:**
    *   **SerializableMultiProjectionState:**
        *   `SerializableMultiProjectionState` インスタンスを作成し、シリアライズ・デシリアライズが正しく行えることを確認する。
        *   Orleansのテスト環境 (`SekibanOrleansTestBase`) を利用し、`SerializableMultiProjectionState` を含むMultiProjectionの状態を永続化（保存）する。
        *   永続化された状態をリストアし、内容が保存前と同一であることを確認する。
    *   **MultiProjectorPayload:**
        *   `MultiProjectorPayload` を使用するMultiProjectionを定義するテストシナリオを作成する。
        *   関連するコマンドを実行し、`MultiProjectorPayload` の状態を更新する。
        *   更新されたMultiProjectionの状態を永続化（保存）する。
        *   永続化された状態をリストアし、`MultiProjectorPayload` 内の各プロジェクターの状態が正しく復元されていることを確認する。
    *   **AggregateListProjector<BranchProjector>:**
        *   `Branch` Aggregate と `BranchProjector` を使用するテストシナリオを作成する。
        *   `AggregateListProjector<BranchProjector>` をMultiProjectionとして使用する。
        *   `RegisterBranch` や `ChangeBranchName` などのコマンドを実行し、Aggregateリストの状態を更新する。
        *   更新された `AggregateListProjector` の状態を永続化（保存）する。
        *   永続化された状態をリストアし、リスト内の `Branch` Aggregateの状態が正しく復元されていることを確認する。

3.  **テストフレームワークと基底クラスの選定:**
    *   `SerializableMultiProjectionState` がOrleansプロジェクトに存在し、保存・リストア機能もおそらくOrleansの永続化を利用していると考えられるため、`Sekiban.Pure.Orleans.xUnit` の `SekibanOrleansTestBase<T>` をテスト基底クラスとして使用する。これにより、OrleansのGrainや永続化機能を含めたテストが可能になる。

4.  **テストファイルの構成:**
    *   `tests/Pure.Domain.xUnit` プロジェクト内に、新しいテストファイル `MultiProjectionPersistenceTests.cs` を作成する。
    *   各テストシナリオに対応するテストメソッドを `[Fact]` 属性で定義し、メソッド名はテスト内容がわかるように命名する (例: `CanSaveAndRestore_SerializableMultiProjectionState`, `CanSaveAndRestore_MultiProjectorPayload`, `CanSaveAndRestore_AggregateListProjector`)。

5.  **テストコードの実装:**
    *   `SekibanOrleansTestBase<T>` が提供するメソッド (`GivenCommandWithResult`, `WhenCommandWithResult`, `ThenGetMultiProjectorWithResult` など) を使用して、コマンド実行、イベント適用、プロジェクションの状態取得を行う。
    *   `issue583_01_restore.md` で実装された保存・リストア処理をテスト内で実行する。`SekibanOrleansTestBase` は通常、テストクラスごとにOrleansのインメモリクラスターをセットアップするため、標準的なGrainの永続化がテストできるはず。特定の保存・リストア用メソッドが追加されている場合は、それらを呼び出す。
    *   リストア後、`ThenGetMultiProjectorWithResult` などでプロジェクションの状態を取得し、`Assert.Equal` や `Assert.True` などを用いて、期待される状態と一致するか検証する。特にリストアされたペイロードの内容（プロパティ値、リストの要素数など）を詳細に確認する。

**質問:**

*   `issue583_01_restore.md` で実装されたMultiProjectionの保存・リストア機能は、具体的にどのような方法で行われますか？ (例: Orleansの標準的なGrain永続化、特定のサービスやメソッドの呼び出しなど)。この情報により、テストコードで適切な保存・リストア処理を呼び出すことができます。現状はOrleansの標準的な永続化を前提として計画を進めます。
