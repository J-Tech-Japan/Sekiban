# MultiProjection OOM対策設計（Streaming Snapshot I/O）

## 背景
けんばいPROD（Azure Container Apps）で `MultiProjectionGrain` のキャッチアップ中に OOM が多発している。
現在の実装は、スナップショット永続化時に最終的なスナップショット全体を `byte[]` としてメモリ上に構築してから保存するため、
状態サイズが大きいProjectionではピークメモリが急増しやすい。

## 目的
- スナップショット永続化のピークメモリを削減する。
- Cosmos/Blob Offload経路を含めて「保存先変更」ではなく「生成・搬送方式」を改善する。
- Orleans/Native/WASMを含む共通のスナップショットI/O境界を拡張可能にする。

## 非目的
- Projectionのドメインロジック（`Project()`のアルゴリズム）自体の刷新。
- 既存の外部API（`IMultiProjectionGrain` など）の破壊的変更。
- 一度のリリースで全ストレージ実装を同時に完全移行すること。

## 現状のボトルネック
1. `MultiProjectionGrain -> host.GetSnapshotBytesAsync()` が `byte[]` 全量生成を前提。
2. `IMultiProjectionStateStore.UpsertAsync(MultiProjectionStateRecord)` が `StateData: byte[]?` 前提。
3. `CosmosMultiProjectionState` は `StateData` を Base64 文字列化して保持（追加メモリを消費）。
4. Blob Offload は `Upsert` の後段で行われるため、オフロード前のピークメモリは下がらない。
5. `IBlobStorageSnapshotAccessor` が `WriteAsync(byte[])` / `ReadAsync(): byte[]` のため、ストリーム転送不可。

## 設計方針
「Snapshotを全量 `byte[]` として扱う境界」を段階的に廃止し、
`Stream` / 一時ファイルベースのパイプラインを導入する。

## 提案アーキテクチャ

### 1) 新しいSnapshot I/O抽象（追加）
既存インターフェースは維持しつつ、新規でストリーム経路を追加する。

- `ISnapshotPayloadWriter`
  - `Task<SnapshotPayloadHandle> WriteAsync(Func<Stream, Task> writeBody, SnapshotWriteOptions options, CancellationToken ct)`
- `ISnapshotPayloadReader`
  - `Task<ResultBox<SnapshotReadResult>> ReadAsync(SnapshotPayloadHandle handle, CancellationToken ct)`

`SnapshotPayloadHandle` は以下を保持:
- 保存先種別（Inline/Blob）
- オフロードキー
- payload length
- content type/version

### 2) Blob accessorのストリーム対応
`IBlobStorageSnapshotAccessor` を拡張:
- `Task<string> WriteAsync(Stream data, string projectorName, CancellationToken ct)`
- `Task<Stream> OpenReadAsync(string key, CancellationToken ct)`

既存 `byte[]` メソッドは当面残し、内部で Stream 実装に委譲する（互換維持）。

### 3) StateStoreの保存経路分離
`IMultiProjectionStateStore` を段階移行:
- 現行 `UpsertAsync(MultiProjectionStateRecord)` は維持
- 追加で `UpsertFromStreamAsync(MultiProjectionStateWriteRequest)` を導入

`MultiProjectionStateWriteRequest` は `StateData` を必須にしない。
- Inline保存なら小サイズのみ `StateData` を許可
- しきい値超過時は Blob へ直接 stream upload し、DBには `IsOffloaded/OffloadKey` のみ保存

### 4) Grain側の一時ファイルベース永続化
`MultiProjectionGrain.PersistStateAsync()` で:
1. スナップショットを直接一時ファイルへ出力（例: `/tmp/sekiban-snapshots/...`）
2. ファイルサイズ判定
3. 小サイズはInline、閾値超は Blob に stream upload
4. DBへメタデータ保存
5. 一時ファイル削除（finally）

重要点:
- ファイル化は「I/O増」だが、OOM回避を優先。
- `/tmp` 容量超過対策として最大同時ファイル数とサイズガードを設ける。

### 5) Cosmosモデル最適化
`CosmosMultiProjectionState.StateData` の利用を縮小。
- デフォルト方針: 一定サイズ以上は常に offload
- Base64保持は小サイズ互換用途に限定

## 段階的移行計画

### Phase 1（低リスク）
- Blob accessor に Stream API追加（既存API温存）
- StateStore実装（Cosmos/Postgres/Dynamo）で Stream Upsert経路を追加
- Feature flagで新経路を無効/有効切替可能にする

### Phase 2（本命）
- Orleans `MultiProjectionGrain` を stream/temp-file persist に切替
- OOM観点のメトリクス追加
  - snapshot build ms
  - snapshot upload ms
  - temp file size
  - persist時GC前後メモリ

### Phase 3（整理）
- `byte[]` 依存コードの縮退
- 互換メソッドのdeprecate
- ドキュメント更新

## 互換性
- 既存レコード形式（`IsOffloaded`, `OffloadKey`, `StateData`）は維持。
- 読み出し側は旧データ（inline）・新データ（offloaded）の両方をサポート。
- ロールバック時に旧コードでも最低限読めるよう、移行期間は `PayloadType` バージョンを明示。

## 失敗時動作
- Blob upload失敗: DB書き込みを行わず失敗を返す。
- DB upsert失敗（upload成功後）: 冪等キーで再試行可能にする。
- 一時ファイル削除失敗: 警告ログ + 次回起動時クリーンアップ。

## 観測・運用
- 追加メトリクス
  - `projection_snapshot_tempfile_bytes`
  - `projection_snapshot_upload_bytes`
  - `projection_snapshot_offload_ratio`
  - `projection_persist_peak_managed_memory_bytes`
- 推奨初期設定
  - `CatchUpBatchSize` を小さめ（200-500）
  - `OffloadThresholdBytes` をCosmos item制限より十分低く設定
  - 大型Projectionはoffload強制

## リスク
- 追加I/Oにより永続化レイテンシが増える可能性。
- ストリームAPIを各ストレージで実装する工数が大きい。
- 失敗補償（upload済み/DB未反映）の整合性設計が必要。

## レビュー論点
1. 新規抽象（Stream I/O）を `Core` に置く妥当性
2. `/tmp` 利用方針（セキュリティ・容量・GCタイミング）
3. CosmosでのInline縮小方針と移行互換性
4. Phase分割でのリスク許容範囲

## 受け入れ基準
- 100MB級スナップショット永続化で OOM を発生させないこと。
- 同一データセットで現行比ピークメモリを有意に削減できること。
- 既存の復元互換（旧stateData/新offload）を維持すること。
