# PR Summary

この PR は **Unsafe Window Materialized View** の設計議論用 PR です。実装は含みません。

## 追加したもの

- `tasks/unsafe-window-materialized-view/design.md`
  - safe / unsafe / merged view の基本設計
  - current MV との関係と `Unsafe Window MV` 一本化方針
  - key-aware contract と `projection key -> tags` replay 要件
  - fan-out / promotion transaction / runtime validation / internal fast path の考え方
  - tombstone ベースの delete / recreate 対応
  - performance / rollout / dynamic safe window の考え方

- `tasks/unsafe-window-materialized-view/open-questions.md`
  - contract
  - promotion worker
  - key-based replay
  - SQL authoring 制約
  - degraded mode / dynamic safe window / tombstone purge

## この PR でレビューしてほしいこと

1. `unsafe` を各 key 1 行とする前提で良いか
2. correctness を safe promotion 時 replay に寄せる設計が妥当か
3. public model を `Unsafe Window MV` に一本化し、simple は内部 fast path として吸収する方針が妥当か
4. v1 で「1 event -> 複数 key fan-out」をどこまで許容するか
5. template は補助、runtime validation / fail-fast を主体にする方針が妥当か

## 今回のレビュー反映点

- `incoming SUID < unsafe SUID` を `_needs_rebuild = true` 必須ルールとして明文化
- replay API に `ProjectionKey -> tag(s)` と incremental replay 開始点を追加
- `Simple MV` / `Unsafe MV` の併存ではなく、single public model + internal fast path を推奨と明記
- v1 でも key ごとに独立 replay できる fan-out projector を許容
- delete は tombstone row を標準表現とし、`current` / `current_live` の責務を分離
- template 依存ではなく、schema validation / fail-fast / DDL 生成案を主軸に整理
