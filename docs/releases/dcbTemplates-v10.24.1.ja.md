# DCB テンプレート 10.24.1

Sekiban DCB 10.24.1 用の 5 つのスターターテンプレートです。生成したプロジェクトは 10.24.1 のパッケージと Orleans 10.4.2 を参照します。10.24.0 のテンプレートは公開していません。このノートは、テンプレート 10.23.4 からの変更をまとめたものです。

## 含まれるライブラリの変更

- 10.24.0：生成したプロジェクトは Orleans 10.4.2 を使います。動いているシステムを更新する前に、10.24.0 のライブラリのリリースノートを読んでください。Orleans クラスタは全体を同時に更新する必要があります。Orleans のメトリクス名の一部が変わりました。Orleans のスクリプトで作った既存の PostgreSQL データベースには、デプロイガイドにあるデータを消さない更新手順が必要です。
- 10.24.1：ライブラリの依存パッケージのセキュリティ更新です。`Sekiban.Dcb.Sqlite` から、脆弱性のある `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 が入らなくなりました。`Sekiban.Dcb.CosmosDb` から、脆弱性のある `Microsoft.AspNetCore.WebUtilities` 2.3.0 が入らなくなりました。詳しくは 10.24.1 のライブラリのリリースノートを見てください。

## テンプレートの変更

- 依存パッケージを現在のバージョンにしました。Aspire 13.6.5、OpenTelemetry 1.20、ASP.NET Core と EF Core と Microsoft.Extensions 10.0.15、Microsoft.Extensions.Http.Resilience と ServiceDiscovery 10.11.0、Scalar 2.20.0 です。ほかのパッケージも、新しいパッチまたはマイナーバージョンにしました。
- 新しいプロジェクトをビルドしても、既知の脆弱性の warning は出ません。以前のバージョンでは、脆弱性のある MessagePack、Microsoft.OpenApi、OpenTelemetry のパッケージが入っていました。10.24.1 より前のライブラリでは、脆弱性のある SQLite と WebUtilities のパッケージも入っていました。
- 2 つの AWS テンプレート（`sekiban-dcb-orleans-aws`、`sekiban-dcb-decider-aws`）の `ApiService/SqlScripts` に、Orleans 10.4.2 用の PostgreSQL スクリプトを入れました。`PostgreSQL-Clustering.sql` ではメンバーシップの関数と、停止したサイロを片付けるクエリが変わりました。`PostgreSQL-Persistence.sql` は `timestamptz` と `now()` を使い、`DeleteStorageKey` のクエリが加わりました。新しく作るデータベースは、このスキーマになります。
- 新しいプロジェクトをビルドしても、コンパイラの warning は出ません。API サービスは、非推奨の `WithOpenApi()` を呼ばなくなりました。OpenAPI ドキュメントには、これまでどおりエンドポイントが載ります。必要な Event Hub やストレージの接続設定がない場合、API サービスは起動時に、その設定の名前を示して停止します。
- Decider テンプレートでは、API サービスが healthy と表示され、2 つの Web フロントエンドが起動するようになりました。以前は、API サービスが待ち受けていない HTTPS のエンドポイントで AppHost がヘルスチェックをしていたため、API サービスが unhealthy のままになり、Web フロントエンドはその完了を待ち続けていました。Blazor の Web フロントエンドは、ダッシュボードのリンクから開けるようになりました。
- すべての AppHost の `http` 起動プロファイルに `ASPIRE_ALLOW_UNSECURED_TRANSPORT` を設定しました。`dotnet run --launch-profile http` で起動できます。
- AppHost プロジェクトで、Aspire 13.6 が推奨する `AspireUseCliBundle` を `true` にしました。AppHost を `dotnet run` で起動すると、Aspire CLI を通して起動します。`aspire` コマンド（13.5.0 以降）がインストールされていない場合は、ビルドのときに同じバージョンの Aspire CLI を `dnx` で取得します。

## 既存のプロジェクト

テンプレートパッケージを更新しても、生成済みのプロジェクトは変わりません。ライブラリの変更を取り込むには、`SekibanDcbTemplateVersion.props` の `SekibanDcbVersion` を `10.24.1` に、`MicrosoftOrleansVersion` を `10.4.2` に変えてください。脆弱性の warning をなくすには、上と同じパッケージを更新してください。そのあと restore が NU1605（パッケージのダウングレード）で失敗する場合は、表示された直接参照を、10.24.1 のライブラリのリリースノートにあるバージョン以上に上げてください。AWS のテンプレートから生成したプロジェクトでは、新しく生成したプロジェクトから `ApiService/SqlScripts` の `PostgreSQL-Clustering.sql` と `PostgreSQL-Persistence.sql` もコピーしてください。既存のデータベースは、デプロイガイド（`docs/dcb_llm/16_deployment.md`）の「Orleans 10.4.2 PostgreSQL upgrade」の手順で更新してください。初期化し直すと grain の状態が消えるので、初期化し直さないでください。
