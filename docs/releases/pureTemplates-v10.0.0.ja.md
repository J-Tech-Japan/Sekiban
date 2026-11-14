# Sekiban.Pure.Templates 10.0.0

Sekiban.Pure.Templates 10.0.0 は、Sekiban.Pure の2つのプロジェクトテンプレート `sekiban-orleans-aspire` と `sekiban-dapr-aspire` を .NET 10 と Sekiban.Pure 10.0.0 に対応させます。`net9.0` のプロジェクトを生成し、プレビュー版パッケージ `1.0.1-preview21` を参照していた 1.1.5 を置き換えます。**Sekiban.Pure はメンテナンスモード（セキュリティ修正のみ）です。新規プロジェクトには Sekiban DCB を推奨します。**

## インストール

```
dotnet new install Sekiban.Pure.Templates::10.0.0
dotnet new sekiban-orleans-aspire -n MyApp
dotnet new sekiban-dapr-aspire -n MyApp
```

.NET 10 SDK が必要です。以前に生成したプロジェクトは変更されません。

## 変更点

- **ターゲットフレームワークとパッケージ:** 生成されるプロジェクトは `net10.0` を対象とし、`Sekiban.Pure*` 10.0.0 を参照します。
- **依存パッケージ:** フレームワークに合わせて更新し、このリリース時点で、生成されたアプリが既知の脆弱性の警告なしに復元できるようにしました。Orleans 10.4.2、.NET Aspire 13.6.5、OpenTelemetry 1.16.0、Scalar.AspNetCore 2.10.3、Microsoft.AspNetCore.OpenApi と Microsoft.Extensions.Logging.Abstractions 10.0.15 です。
- **廃止予定の呼び出し:** Orleans の API サービスは、廃止予定の Azure クライアント登録メソッドを呼ばなくなりました。
- **デプロイ用ファイル:** Dockerfile、デプロイスクリプト、Bicep ファイルは .NET 10 を指します。
- **README:** パッケージの README にある Orleans の設定の節を、テンプレートの AppHost に合わせました。

## 確認したこと

- CI で、パックしたパッケージから2つのテンプレートを生成し、既知の脆弱性の警告（NU1900 から NU1905）をエラー扱いにしてビルドし、ユニットテストが通ることを確認しています。
- テンプレートから生成した Orleans アプリを Docker で起動しました（Aspire が PostgreSQL と Azure Storage のコンテナを起動します）。コマンドが受理され、追加した予報がクエリ API で読み戻せ、件数クエリ、Blazor ページ、ヘルスエンドポイントが 200 で応答しました。

## 確認していないこと

- **Dapr。** Dapr テンプレートは復元とビルドができ、ユニットテストも通りますが、これらのバージョンで Dapr ランタイム（placement サービス、scheduler、サイドカー）に対して最後まで動かしてはいません。
- **.NET 11 SDK。** 生成されるプロジェクトは `net10.0` を対象とし、ビルドとテストは .NET 10 SDK だけで行いました。.NET 11 SDK ではビルドしていません。11.0.100 で試した1回では、素の `net10.0` の Blazor アプリでさえビルドできませんでした。
- **デプロイ用ファイルとブラウザテスト。** Bicep ファイル、デプロイスクリプト、Dockerfile は実行しておらず、Orleans テンプレートの Playwright テストも実行していません。

## パッケージ

`Sekiban.Pure.Templates` 10.0.0。テンプレートは `sekiban-orleans-aspire` と `sekiban-dapr-aspire` です。
