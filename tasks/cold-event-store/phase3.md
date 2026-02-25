# Phase 3 設計（マルチフォーマット + Stream/Change Feed 拡張）

## スコープ

- JSONL 正本 + DuckDB/SQLite 派生 artifact 生成
- Stream/Change Feed 取り込みオプション
- compaction・ライフサイクル管理

## 実装対象

1. 形式拡張
- `IColdSegmentWriter` 実装
  - `JsonlSegmentWriter`（正本）
  - `DuckDbSegmentWriter`（派生）
  - `SqliteSegmentWriter`（派生）

2. 取り込み経路拡張
- `IColdIngestionMode`:
  - Pull
  - ChangeFeed/Stream
- ChangeFeed 時も SafeWindow 判定を共通化

3. 保守機能
- segment compaction（小segment統合）
- retention（古世代削除）
- manifest 世代管理（rollback 可能）

## 同時更新戦略（強化）

- manifest を append-only journal + head pointer 方式へ拡張可能にする
- head 更新のみ CAS で制御
- 失敗時は前世代 head にロールバック

## テスト

- JSONL から DuckDB/SQLite 派生生成の整合性
- compaction 後も from/to 範囲が連続
- ChangeFeed 再送があっても重複取り込みしない

## 完了条件

- JSONL/SQLite/DuckDB の選択または併用が可能
- Pull と ChangeFeed どちらでも同じ整合性ルールを満たす
- 長期運用で control file と segment の整合を維持できる
