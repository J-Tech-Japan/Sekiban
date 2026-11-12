# Sekiban.Pure 10.0.0

Sekiban.Pure 10.0.0 は、Sekiban.Pure を .NET 10 と .NET 11 に対応させる互換リリースです。**機能追加はなく、保存データも変わりません。9.0.0 との API の違いは、Orleans の xUnit テスト基底クラスの 2 点だけです（「アップグレード前に」を参照）。** 12 個のパッケージは、すべて 10.0.0 として同時にリリースします。

## サポート方針

Sekiban.Pure はメンテナンスモードです。このリリース以降は、セキュリティ修正のみを行います。対象は、Sekiban.Pure 自身の脆弱性の修正と、公開済みのセキュリティ勧告を解消する依存関係の更新です。12 個のパッケージは、既知の脆弱性について CI で監査します（Pure に触れるプルリクエストと、毎週）。新規プロジェクトには Sekiban DCB を推奨します。.NET 9 向けには、Sekiban.Pure 9.0.0 が nuget.org に引き続き残ります。

## 変更点

- **ターゲットフレームワーク：** `net10.0` と `net11.0`。`net9.0` は対象外になりました。`Sekiban.Pure.SourceGenerator` は引き続き `netstandard2.0` です。
- **依存関係：**

| 依存関係 | 9.0.0 | 10.0.0 |
|---|---|---|
| Microsoft.Orleans.* | 9.2.1 | 10.4.2 |
| Microsoft.EntityFrameworkCore | 9.0.8 | 10.0.15 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 9.0.4 | 10.0.3 |
| Microsoft.Extensions.* と System.Configuration.ConfigurationManager | 9.0.8 | 10.0.15 |
| ResultBoxes | 0.3.31 | 0.4.0 |

  変更なし：Dapr 1.15.4、Microsoft.Azure.Cosmos 3.53.1、Newtonsoft.Json 13.0.3。新規：`Sekiban.Pure.Orleans.xUnit` が Humanizer.Core 2.14.1 に依存するようになりました。

## アップグレード前に

1. **Orleans 9 から 10 へは、クラスタ全体を同時に更新します。** Sekiban.Pure 9.x（Orleans 9）と Sekiban.Pure 10.0.0（Orleans 10）のサイロを、同じクラスタで混在させないでください。すべてのサイロとクライアントを同時に更新し、Orleans 10 のアップグレード手順に従ってください。
2. **Orleans 10 は、既定の JSON グレイン状態シリアライザーに型の許可リストを適用します。** ホストが Sekiban のグレイン状態を Orleans の既定の JSON シリアライザーで永続化している場合、読み込みが `CodecNotFoundException` で失敗することがあります。`docs/llm/10_orleans_setup.md` とサンプルにあるとおり、`NewtonsoftJsonSekibanOrleansSerializer` を登録してください。`Sekiban.Pure.Orleans.xUnit` と `Sekiban.Pure.Orleans.NUnit` の Orleans テスト基底クラスは、すでにこれを行っています。
3. **`Sekiban.Pure.Orleans.xUnit` のソースレベルの変更が 2 点あります。** 対象は `SekibanOrleansTestBase<TDomainTypesGetter>` です。公開 API を 9.0.0 と比較して見つかった違いは、この 2 点だけです。
   - `Configure(ISiloBuilder)` は、`ISiloConfigurator` の明示的なインターフェイス実装になりました。テスト基底クラスの型から直接呼んでいるコードは、`ISiloConfigurator` 経由で呼ぶ必要があります。
   - `ConfigureServices(IServiceCollection)` は `protected virtual` になりました（以前は `public virtual`）。オーバーライドは `protected override` で宣言してください。
4. **フレームワークの変更とパッケージの更新は、一緒に行います。** プロジェクトを `net10.0` か `net11.0` に変更し、`Sekiban.Pure.*` のパッケージ参照をすべて同時に 10.0.0 に更新してください。
5. **ResultBoxes 0.4.0。** ResultBoxes の型は、Sekiban.Pure の公開 API に現れます。自分のコードで ResultBoxes を直接使っている場合は、アップグレード前に 0.4.0 の変更点を確認してください。

## 確認したこと

- **公開 API：** 各パッケージを、公開済みの 9.0.0 のパッケージと net10.0・net11.0 の両方で比較しました（23 回の比較）。見つかったのは、上の 2 点だけです。
- **保存データ：** 9.0.0 が書いたイベント、スナップショット、Orleans のグレイン状態、Cosmos DB のイベントドキュメントを 10.0.0 が正しく読めること、また 10.0.0 が同じバイト列を書くことを確認しました（gzip 圧縮されたスナップショットにある、OS を示す 1 バイトのヘッダーだけは除きます）。
- **PostgreSQL：** 9.0.0 で作ったデータベースと、10.0.0（.NET 10 と .NET 11）で作ったデータベースのスキーマは同一で、未適用のマイグレーションやモデルの変更はなく、どちらのバージョンが書いた行も、もう一方が同じように読めます。
- **テスト：** Pure のすべてのテストプロジェクトが、CI の net10.0 と net11.0 で通っています（PostgreSQL と Cosmos DB のテストを含みます）。
- **サンプル：** `OrleansSekiban`（Orleans と PostgreSQL）、`MemStat.Net.Web`、`MemStat.Console` は、新しいバージョンで起動し、動作します。

## 確認していないこと

- **Dapr。** `Sekiban.Pure.Dapr` のユニットテストは net10.0 と net11.0 で通っていますが、このリリースでは Dapr のサイドカーを起動しておらず、Dapr のサンプル（`DaprSample`、`DaprSample2`）も、新しいバージョンでは実行していません。Dapr のアクターで Sekiban.Pure を動かしている場合は、本番を更新する前に、ご自身の環境で確認してください。
- **テンプレート。** `Sekiban.Pure.Templates` は、このリリースでは更新していません。引き続き、`net9.0` のプロジェクトを生成し、古いプレビュー版のパッケージ（`1.0.1-preview21`）を参照します。

## パッケージ

`Sekiban.Pure`、`Sekiban.Pure.AspNetCore`、`Sekiban.Pure.CosmosDb`、`Sekiban.Pure.Dapr`、`Sekiban.Pure.NUnit`、`Sekiban.Pure.Orleans`、`Sekiban.Pure.Orleans.NUnit`、`Sekiban.Pure.Orleans.xUnit`、`Sekiban.Pure.Postgres`、`Sekiban.Pure.ReadModel`、`Sekiban.Pure.SourceGenerator`、`Sekiban.Pure.xUnit`（すべて 10.0.0）。
