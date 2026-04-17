# Unsafe Window Materialized View - Open Questions

## 1. Contract

- 新しい contract を `Unsafe Window MV` の唯一の public contract としてどう整えるか
- `BuildUnsafeRowAsync` と `RebuildSafeRowAsync` を分けるか、1 メソッドに統合するか
- `Apply(TRow? current, Event ev, ApplyContext ctx)` の単一 primitive に寄せるか
- `TRow` を POCO に限定するか
- 1 event から 0..n 個の `ProjectionKey` を返す fan-out を v1 contract に入れるか

## 2. Key モデル

- `projection key` と table PK を常に一致させるか
- aggregate key と projection key が異なるケースを v1 で許容するか
- key-based replay の API を event store にどう追加するか
- `ProjectionKey -> tag(s)` の対応を projector contract にどう載せるか
- replay を `safe row._last_sortable_unique_id` 以降の incremental read 前提に固定するか

## 3. Promotion Worker

- dedicated background worker にするか
- grain / actor / hosted service のどこで担うか
- 1 service instance だけが昇格を行う制御をどう置くか
- `FOR UPDATE SKIP LOCKED` + 1 transaction を標準にするか
- `_in_promotion_at` による stale lock 回収をどう設計するか
- promotion 完了後の遅延 stream event をどう idempotent に扱うか

## 4. Rebuild Policy

- 順序逆転時に即 unsafe rebuild するか
- `_needs_rebuild` を立てるだけで promotion 時まで遅延するか
- safe promotion 時に常に full replay するか、fast path 差分 replay を持つか
- `incoming.SUID < unsafe.SUID` なら `_needs_rebuild = true` を必須規約にするか

## 5. SQL Authoring

- v1 はテンプレートベースに制限すべきか
- 完全自由 SQL を public contract でどこまで許容するか
- startup schema validation を fail-fast にするか
- `[MvColumn]` 付き POCO から DDL を生成する方向に寄せるか

## 6. Read API / Diagnostics

- merged view のみ公開するか
- safe / unsafe を診断 API で分けて返すか
- current row が safe 由来か unsafe 由来かのフラグを公開するか
- `_is_deleted` tombstone を current view で返すか、consumer 向け view で隠すか
- delete 後の recreate と tombstone retention をどう定義するか

## 7. Performance

- safe window の推奨値を framework が持つか
- projector の `SafeWindow` を baseline とし、dynamic extra window を framework が加算するか
- key ごとの高頻度更新で rebuild が多発した場合の backpressure をどう置くか
- promotion batch size / parallelism をどう制御するか
- `unsafe_count`, `oldest_unsafe_age`, `promotion_lag` を必須メトリクスにするか
- `unsafe_count` 急増時の degraded mode や partial index を v1 から考慮するか

## 8. Rollout

- まず Postgres 限定にするか
- 既存 materialized view 実装を legacy / bridge としてどう扱うか
- package / namespace を最初から Unsafe Window MV 前提でどう切るか
- PoC を `WeatherForecast` で始めた後、`ClassRoomEnrollmentMvV1` の fan-out projector で検証するか
