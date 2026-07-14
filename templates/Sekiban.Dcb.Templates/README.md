# Sekiban DCB Templates

Sekiban DCB (Dynamic Consistency Boundary) 向けの .NET Aspire + Orleans スターターテンプレートです。

生成されるプロジェクトはすべて **.NET 10.0** / **Aspire 13.x** / **Sekiban.Dcb 10.3.0** です。

## インストール

```bash
dotnet new install Sekiban.Dcb.Templates
```

## テンプレート一覧

| ショート名 | 結果モデル | イベントストア | スナップショット | クラウド |
|---|---|---|---|---|
| `sekiban-dcb-orleans` | WithResult (`ResultBox`) | Cosmos DB / Postgres / SQLite | Azure Blob | Azure |
| `sekiban-dcb-orleans-withoutresult` | WithoutResult (例外ベース) | Cosmos DB / Postgres / SQLite | Azure Blob | Azure |
| `sekiban-dcb-orleans-aws` | WithoutResult (例外ベース) | DynamoDB / Postgres | Amazon S3 | AWS |
| `sekiban-dcb-decider` | WithoutResult (Decider パターン) | Cosmos DB / Postgres / SQLite | Azure Blob | Azure |
| `sekiban-dcb-decider-aws` | WithoutResult (Decider パターン) | DynamoDB / Postgres | Amazon S3 | AWS |

イベントストアは生成後に `Sekiban:Database` 設定で切り替えます(既定は Postgres)。

```bash
dotnet new sekiban-dcb-orleans -n YourProjectName
```

各テンプレートに共通して含まれるもの:

- Orleans クラスタ + .NET Aspire AppHost
- API Service / Web フロント (Blazor Server) / ServiceDefaults
- ドメインモデルとマルチプロジェクション、マテリアライズドビュー
- ユニットテストプロジェクト (`--IncludeTests false` で除外可能)

## Cosmos DB を使う場合

生成されるプロジェクトは **`CosmosWriteFailurePolicy.RollForward`** を明示的に選択します。新規デプロイにはこれが適切なポリシーです。ライブラリの既定値が `Compatible` のままなのは、既存デプロイの挙動をパッケージ更新だけで変えないためであり、テンプレートが生成するのは新規デプロイだからです。

ただし **RollForward はクラッシュウィンドウを閉じません**。Cosmos はイベントとタグ行を2段階で書き込むため、クラッシュするとイベントだけが永続化され、タグ行が欠落することがあります(all-events 読み取りからは見えるが、タグ検索からは見えない)。これを閉じられるのは修復パスだけです。

修復サービスとスイープは **どちらも opt-in** で、生成コードには**コメントとしてのみ**記載しています(テンプレートが生成したというだけで、あなたのコンテナーのスキャンを勝手に始めるべきではないため)。

詳細は [ストレージプロバイダーの整合性契約](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm_ja/11_storage_providers.md#整合性契約) と [トラブルシューティング](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm_ja/13_common_issues.md) を参照してください。

## ローカル開発 / ユニットテスト

生成されるプロジェクトはローカルでも **Orleans**(単一サイロ、localhost クラスタリング)で動きます。インメモリエグゼキューターはユニットテスト専用であり、`*.Unit` プロジェクトだけが `Sekiban.Dcb.*.Testing` パッケージを参照します。ランタイムプロジェクトからは参照しないでください。

分類と、CLI / Worker / Web それぞれの localhost 構成は [localhost Orleans ガイド](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm_ja/22_localhost_orleans.md) を参照してください。

## 生成後の手順

```bash
dotnet restore
dotnet run --project YourProjectName.AppHost
```

## Secrets 設定 (Postgres パスワード例)

```json
{
  "Parameters:postgres-password": "your_strong_password"
}
```

## 参照

- Sekiban リポジトリ: <https://github.com/J-Tech-Japan/Sekiban>
- .NET Aspire: <https://learn.microsoft.com/dotnet/aspire>
- Orleans: <https://learn.microsoft.com/dotnet/orleans>
