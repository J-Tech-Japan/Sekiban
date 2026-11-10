# DCB テンプレート 10.24.0

Sekiban DCB 10.24.0 用の 5 つのスターターテンプレートです。生成したプロジェクトは 10.24.0 のパッケージと Orleans 10.4.2 を参照します。

## 含まれるライブラリの変更

生成したプロジェクトは Orleans 10.4.2 を使います。動いているシステムを更新する前に、10.24.0 のライブラリのリリースノートを読んでください。Orleans クラスタは全体を同時に更新する必要があります。Orleans のメトリクス名の一部が変わりました。Orleans のスクリプトで作った既存の PostgreSQL データベースには、デプロイガイドにあるデータを消さない更新手順が必要です。

## テンプレートの変更

- 依存パッケージを現在のバージョンにしました。Aspire 13.6.4、OpenTelemetry 1.20、ASP.NET Core と EF Core と Microsoft.Extensions 10.0.14、Microsoft.Extensions.Http.Resilience と ServiceDiscovery 10.11.0、Scalar 2.19.0 です。ほかのパッケージも、新しいパッチまたはマイナーバージョンにしました。
- 新しいプロジェクトをビルドしても、既知の脆弱性の warning は出ません。以前のバージョンでは、脆弱性のある MessagePack、Microsoft.OpenApi、OpenTelemetry のパッケージが入っていました。
- Azure のテンプレートは `Microsoft.Data.Sqlite` 10.0.14 を直接参照します。`Sekiban.Dcb.Sqlite` 10.24.0 は 10.0.3 に依存していて、high の既知の脆弱性（GHSA-2m69-gcr7-jv3q）がある `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 が入るためです。
- 新しいプロジェクトをビルドしても、コンパイラの warning は出ません。API サービスは、非推奨の `WithOpenApi()` を呼ばなくなりました。OpenAPI ドキュメントには、これまでどおりエンドポイントが載ります。必要な Event Hub やストレージの接続設定がない場合、API サービスは起動時に、その設定の名前を示して停止します。
- Decider テンプレートでは、API サービスが healthy と表示され、2 つの Web フロントエンドが起動するようになりました。以前は、API サービスが待ち受けていない HTTPS のエンドポイントで AppHost がヘルスチェックをしていたため、API サービスが unhealthy のままになり、Web フロントエンドはその完了を待ち続けていました。Blazor の Web フロントエンドは、ダッシュボードのリンクから開けるようになりました。
- すべての AppHost の `http` 起動プロファイルに `ASPIRE_ALLOW_UNSECURED_TRANSPORT` を設定しました。`dotnet run --launch-profile http` で起動できます。
- AppHost プロジェクトで、Aspire 13.6 が推奨する `AspireUseCliBundle` を `true` にしました。AppHost を `dotnet run` で起動すると、Aspire CLI を通して起動します。`aspire` コマンド（13.5.0 以降）がインストールされていない場合は、ビルドのときに同じバージョンの Aspire CLI を `dnx` で取得します。

## 既存のプロジェクト

テンプレートパッケージを更新しても、生成済みのプロジェクトは変わりません。ライブラリの変更を取り込むには、`SekibanDcbTemplateVersion.props` の `SekibanDcbVersion` を `10.24.0` に、`MicrosoftOrleansVersion` を `10.4.2` に変えてください。脆弱性の warning をなくすには、上と同じパッケージを更新してください。`Sekiban.Dcb.Sqlite` を参照しているプロジェクトでは、`Microsoft.Data.Sqlite` 10.0.14 の参照を追加してください。
