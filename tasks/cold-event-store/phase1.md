# Phase 1 設計（MVP: Pull + JSONL + Control Files）

## スコープ

- デフォルト無効の Cold Event Store インターフェース導入
- NotSupported 実装導入
- Pull Export（30分）で Hot Store から JSONL segment 生成
- `manifest/checkpoint/lease` の control ファイル管理

## 実装対象

1. `Sekiban.Dcb.Core`（または新規 `Sekiban.Dcb.ColdEvents`）に以下追加
- `ColdEventStoreOptions`
- `IColdEventStoreFeature`
- `IColdEventProgressReader`
- `IColdEventExporter`
- `IColdEventReader`
- `NotSupportedColdEventStore`

2. ストレージ抽象
- `IColdObjectStorage`（Put/Get/List/Delete + conditional write）
- `IColdLeaseManager`（Acquire/Renew/Release）

3. データ契約
- `ColdManifest`
- `ColdCheckpoint`
- `ColdSegmentInfo`
- `ExportResult`

4. エクスポートジョブ
- `ColdExportHostedService` または Function 実装
- 30分周期（設定可能）

## エクスポートアルゴリズム

1. `AcquireLease(serviceId)`
2. `checkpoint.nextSince` 読み込み（なければ null）
3. Hot Store から増分読取（`ReadAllSerializableEventsAsync`）
4. `cutoff = now - SafeWindow` より新しいイベントを除外
5. 100,000（既定）到達で segment ローテーション
6. segment ファイルアップロード
7. manifest + checkpoint を条件付き更新（ETag/version一致必須）
8. lease 解放

## 失敗時挙動

- lease 取得失敗: 今回スキップ（多重実行防止）
- manifest 更新競合: 再取得して最大3回リトライ
- segment アップロード失敗: checkpoint を更新しない（再実行可能）

## テスト

- 単体
  - NotSupported が `IsSupported=false, IsEnabled=false` を返す
  - SafeWindow フィルタ境界テスト
  - segment ローテーション（100,000 / byte上限）
- 結合
  - 同時2実行で manifest 整合性維持
  - 再実行時に重複登録されない

## 完了条件

- Enabled=false 時に既存パスへ一切影響しない
- manifest 単体取得で最新保存位置を取得可能
- 30分 Pull で安定して増分エクスポート可能
