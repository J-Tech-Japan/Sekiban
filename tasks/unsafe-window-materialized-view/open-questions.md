# Unsafe Window Materialized View - Open Questions

## 1. Contract

- 新しい contract は `IMaterializedViewProjector` の拡張にするか、別 interface にするか
- `BuildUnsafeRowAsync` と `RebuildSafeRowAsync` を分けるか、1 メソッドに統合するか
- `TRow` を POCO に限定するか、SQL statement return も許容するか

## 2. Key モデル

- `projection key` と table PK を常に一致させるか
- aggregate key と projection key が異なるケースを v1 で許容するか
- key-based replay の API を event store にどう追加するか

## 3. Promotion Worker

- dedicated background worker にするか
- grain / actor / hosted service のどこで担うか
- 1 service instance だけが昇格を行う制御をどう置くか

## 4. Rebuild Policy

- 順序逆転時に即 unsafe rebuild するか
- `_needs_rebuild` を立てるだけで promotion 時まで遅延するか
- safe promotion 時に常に full replay するか、fast path 差分 replay を持つか

## 5. SQL Authoring

- v1 はテンプレートベースに制限すべきか
- 完全自由 SQL を unsafe-window mode でも許容するか
- warning に留めるか、fail-fast にするか

## 6. Read API / Diagnostics

- merged view のみ公開するか
- safe / unsafe を診断 API で分けて返すか
- current row が safe 由来か unsafe 由来かのフラグを公開するか

## 7. Performance

- safe window の推奨値を framework が持つか
- key ごとの高頻度更新で rebuild が多発した場合の backpressure をどう置くか
- promotion batch size / parallelism をどう制御するか

## 8. Rollout

- まず Postgres 限定にするか
- 既存 materialized view 実装と同じ package に置くか
- experimental package / namespace に分離するか
