# Sekiban DCB 10.24.0

- バージョン: `10.24.0`。DCB ライブラリは 26 パッケージです。net9.0 と net10.0 を対象とします。
- `Microsoft.Orleans.*` を 10.3.1 から **10.4.2** に上げました。直接の依存の変更はこれだけです。DCB ライブラリのコードと公開 API は 10.23.4 から変わっていません。
- DCB のイベントデータの移行は不要です。

## Orleans 10.4.2

- **Orleans クラスタは全体を同時に更新してください。** Orleans 10.3.1 と 10.4.2 の silo を同じクラスタで動かす構成はサポートされません。ホストで直接参照している `Microsoft.Orleans.*` も、すべて 10.4.2 に揃えてください。
- **Orleans 10.4.0 でメトリクスが変わりました。** `orleans-app-requests-latency-bucket`、`-count`、`-sum` は、ミリ秒（小数）の histogram `orleans-app-requests-latency` に置き換わりました。`orleans-grains` のラベルは `type` から `grain_type` に変わりました。ダッシュボードとアラートを更新してください。詳しくは [Orleans 10.4.0 のリリースノート](https://github.com/dotnet/orleans/releases/tag/v10.4.0)を参照してください。
- **独自の grain interface。** Orleans 10.4.0 では、パラメータに `[Id]` を付けた grain メソッドや、`CancellationToken` が最後の引数でない grain メソッドで、RPC 引数の wire ID が変わりました。DCB 自身の grain は該当しません。該当する grain がある場合は、すべての silo とクライアントを同時に更新してください。
- **推移依存で analyzer が加わります。** Orleans 10.4.2 では、Core、Runtime、Serialization、Streaming.AzureStorage をはじめ、ほとんどのパッケージが `Microsoft.Orleans.Analyzers` と `Microsoft.Orleans.CodeGenerator` に依存します。そのため、Orleans の serialization だけを参照するプロジェクト（ドメインのプロジェクトなど）でも、Orleans の generator と analyzer が動くようになります。
- **Orleans ADO.NET のスクリプトで作った PostgreSQL データベース。** Orleans 10.4.2 では、PostgreSQL の Clustering と Persistence のスクリプトが変わりました。既存のデータベースは、Orleans がインストール済みのクリーンアップ用クエリを使うため、そのまま動きます。ただし、更新するまで新しい動作にはなりません。AWS テンプレートの `OrleansSchemaInitializer` は、既存のデータベースを更新しません。grain の状態と reminder が消えるため、再初期化を強制しないでください。データを消さない更新手順は、デプロイガイド（`docs/dcb_llm_ja/16_deployment.md` の「Orleans 10.4.2 PostgreSQL 更新手順」）にあります。

## テンプレート

10.24.0 のテンプレート（AWS テンプレートの PostgreSQL スクリプトの更新を含む）は、ライブラリの後に、別途リリースします。
