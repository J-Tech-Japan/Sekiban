# Sekiban DCB Templates 10.22.0 リリース本文

これは DCB 10.22.0 リリーストレインにおけるテンプレート用の別個のレビュー済み本文です。
`prepared` のテンプレート段階は、同じ peeled integration commit が `libraries-verified` に到達し、続いて
`template-tagged/incomplete` になった後にだけ開始します。

- テンプレートパッケージ: `Sekiban.Dcb.Templates` 10.22.0。
- 5 つの生成テンプレートの authority と README は DCB 10.22.0 を使用し、Orleans 10.3.1 を維持します。
- 生成 net10 プロジェクト、net9 carrier、PostgreSQL consumer、Azure-free Orleans Core の依存境界を、
  公開前に分離したローカル feed から検証します。

この本文は `artifacts-verified` 段階に渡すレビュー済み入力です。テンプレートの公開、タグや Release の
作成、Issue のクローズ、段階完了を意味しません。
一時的な公開失敗は同じ不変テンプレートタグで再試行できます。ソース変更を伴う復旧には、運用担当者が
承認した新しいバージョンと新たなレビューが必要です。
