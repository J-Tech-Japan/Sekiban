# PR Summary

この PR は **Unsafe Window Materialized View** の設計議論用 PR です。実装は含みません。

## 追加したもの

- `tasks/unsafe-window-materialized-view/design.md`
  - safe / unsafe / merged view の基本設計
  - current MV との関係
  - key-aware contract の方向性
  - performance / rollout の考え方

- `tasks/unsafe-window-materialized-view/open-questions.md`
  - contract
  - promotion worker
  - key-based replay
  - SQL authoring 制約

## この PR でレビューしてほしいこと

1. `unsafe` を各 key 1 行とする前提で良いか
2. correctness を safe promotion 時 replay に寄せる設計が妥当か
3. 現行 MV とは別モードに分ける方針が妥当か
4. v1 を key-aware / row-centric projector に限定するのが妥当か
5. テンプレート + validation + warning の戦略で利用者体験を作れるか
