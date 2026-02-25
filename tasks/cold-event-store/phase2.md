# Phase 2 設計（Hybrid Reader + 運用監視）

## スコープ

- Cold + Hot tail の Hybrid 読み取り導入
- MultiProjection catch-up で Cold 読み取りを利用
- 監視指標と運用コマンド追加

## 実装対象

1. `HybridEventStore : IEventStore` 追加
- 書き込みは既存 Hot Store に委譲
- 読み取りは以下優先順
  1) Cold (`IColdEventReader`)
  2) 不足分のみ Hot Store tail

2. Catch-up 統合
- `MultiProjectionGrain.ProcessSingleCatchUpBatch()` で DI 経由 `IEventStore` を使用済みのため、登録差し替えで適用

3. 監視・可視化
- `latest_safe_lag_seconds`
- `cold_export_duration_ms`
- `cold_manifest_conflict_retries`
- `cold_tail_hot_read_count`

4. 運用 API（任意）
- `GET /cold/progress/{serviceId}`
- `POST /cold/export/{serviceId}`（手動実行）

## 読み取りアルゴリズム（Hybrid）

1. `manifest` から `LatestSafeSortableUniqueId` を確認
2. `since` から `latestSafe` までを Cold から取得
3. `latestSafe` より後を Hot Store から取得
4. 連結後、`SortableUniqueId` で順序保証・重複除去

## 失敗時挙動

- Cold 読み取り失敗: Hot Store フルフォールバック（ログ警告）
- manifest 未取得: Hot Store フルフォールバック
- tail 読み取り失敗: エラー返却（既存挙動に合わせる）

## テスト

- Cold 正常 + tail ありで時系列順が崩れない
- Cold 障害時に Hot フォールバックする
- maxCount 指定時に Cold/Hot 合算でも件数上限を守る

## 完了条件

- 大部分が Cold 経由で読まれ、Hot 読み取りが tail のみに限定される
- 監視指標から lag と競合状態を把握可能
- 本番切替は feature flag で段階展開できる
