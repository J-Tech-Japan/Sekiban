# Orleans構成 - アクターベースで DCB を実行

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [コマンドワークフロー](06_workflow.md)
> - [シリアライゼーションとドメイン型登録](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントUI (Blazor)](09_client_api_blazor.md)
> - [Orleans構成](10_orleans_setup.md) (現在位置)
> - [ストレージプロバイダー](11_storage_providers.md)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)
> - [永続 Orleans サブスクリプション](23_durable_orleans_subscriptions.md)

DCB のアクター実装は Orleans 上で動作します。テンプレートの AppHost は `UseOrleans` を通じてクラスタ設定を構築
します (`internalUsages/DcbOrleans.ApiService/Program.cs`)。

## 対応環境

| 環境 | クラスタリング | Grain ストレージ | ストリーム |
|-----|--------------|-----------------|----------|
| **開発 (Aspire)** | Localhost | Memory | Memory |
| **Azure** | Cosmos DB / Azure Table | Azure Blob | Azure Queue |
| **AWS** | RDS PostgreSQL (ADO.NET) | Memory / Custom | Amazon SQS |

---

## Azure 環境での設定

```csharp
builder.UseOrleans(config =>
{
    if (builder.Environment.IsDevelopment())
    {
        config.UseLocalhostClustering();
    }
    else
    {
        config.UseCosmosClustering(options => options.ConfigureCosmosClient(connectionString));
    }

    config.Configure<ClusterOptions>(opt =>
    {
        opt.ClusterId = "sekiban-dcb";
        opt.ServiceId = "sekiban-dcb-service";
    });
});
```

### Azure ストレージ

- Grain 永続化: Blob/Table/Cosmos から選択 (`ORLEANS_GRAIN_DEFAULT_TYPE`)
- TagState: Grain ストレージにスナップショットを保存可能
- マルチプロジェクション: `IBlobStorageSnapshotAccessor` で Blob Storage に退避

### Azure ストリーム

```csharp
config.AddAzureQueueStreams("EventStreamProvider", configurator =>
{
    configurator.ConfigureAzureQueue(options =>
    {
        options.QueueServiceClient = sp.GetKeyedService<QueueServiceClient>("DcbOrleansQueue");
        options.QueueNames = ["dcborleans-eventstreamprovider-0", "-1", "-2"];
    });
    configurator.ConfigureCacheSize(8192);
});
```

---

## AWS 環境での設定

AWS では Orleans クラスタリングに RDS PostgreSQL (ADO.NET)、ストリームに SQS を使用します。

```csharp
builder.UseOrleans(config =>
{
    if (builder.Environment.IsDevelopment())
    {
        config.UseLocalhostClustering();
        config.AddMemoryStreams("EventStreamProvider");
    }
    else
    {
        // RDS PostgreSQL でクラスタリング
        var rdsConnectionString = BuildRdsConnectionString();
        config.UseAdoNetClustering(options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = rdsConnectionString;
        });
        config.UseAdoNetReminderService(options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = rdsConnectionString;
        });

        // SQS ストリーム
        config.AddSqsStreams("EventStreamProvider", configurator =>
        {
            configurator.ConfigureSqs(options =>
            {
                options.Region = "ap-northeast-1";
                options.QueuePrefix = "orleans-stream-prod";
            });
        });
    }

    config.Configure<ClusterOptions>(opt =>
    {
        opt.ClusterId = "sekiban-dcb";
        opt.ServiceId = "sekiban-service";
    });
});
```

### Orleans スキーマの初期化 (AWS)

RDS PostgreSQL では Orleans のスキーマが必要です。アプリ起動時に自動でスキーマを作成する `OrleansSchemaInitializer` を使用します。

```csharp
var schemaInitializer = new OrleansSchemaInitializer(logger);
await schemaInitializer.InitializeAsync(rdsConnectionString);
```

---

## Grain 実装

- `TagConsistentGrain`: 予約処理 (GeneralTagConsistentActor をラップ)
- `TagStateGrain`: タグ状態キャッシュ (GeneralTagStateActor をラップ)
- `MultiProjectionGrain`: イベント処理とクエリ提供

実装は `src/Sekiban.Dcb.Orleans/Grains/*.cs` を参照。

## エグゼキューター登録

```csharp
builder.Services.AddSingleton<ISekibanExecutor, OrleansDcbExecutor>();
```

`OrleansActorObjectAccessor` が必要な Grain を見つけ、`GeneralSekibanExecutor` に渡します。

## ASP.NET 連携

`AddServiceDefaults()` が OpenTelemetry/HealthCheck/構成バインディングをまとめて設定します。
`app.MapHealthChecks("/health")` を追加して可用性を監視しましょう。

## デプロイ時の注意

### Azure
- `ORLEANS_CLUSTERING_TYPE` を `azuretable` / `cosmos` に設定
- Azure Queue を事前作成するか、プロビジョニング時に `IsResourceCreationEnabled` を有効にする

### AWS
- RDS PostgreSQL でスキーマが自動作成されます
- SQS キューは CDK で事前作成されます
- 環境変数で `Orleans__ClusterId` と `Orleans__ServiceId` を設定

### 共通
- サイロを水平スケールするとタグ Grain が自動で再配置されます

## Grain ディレクトリと重複活性化

Orleans 10.3.1 の既定値は、結果整合性を持つ `LocalGrainDirectory` です。Sekiban のホストとテンプレートは
Grain ディレクトリを構成していないため、この既定値を使います。メンバーシップの変動時には一時的に重複活性化が
発生し得ます。Orleans は重複側を非活性化して解消しますが、共存期間中の書き込みには影響があります。Azure Table や
Cosmos による**クラスタリング**はメンバーシップの構成であり、Grain ディレクトリやイベントストアのフェンスの構成では
ありません。[Orleans の Grain ディレクトリガイド](https://learn.microsoft.com/en-us/dotnet/orleans/host/grain-directory)も参照してください。

### 強整合ディレクトリの試験的なオプトイン

`TagConsistentGrain` と `MaterializedViewGrain` の重複活性化リスクを減らすため、クラスタ内の強整合ディレクトリを
検討してください。10.3.1 の `AddDistributedGrainDirectory` は `Orleans.Hosting.CoreHostingExtensions` の拡張メソッド
（`Microsoft.Orleans.Runtime`）で、`ORLEANSEXP003` が付いた試験的 API です。オプトイン機能であり、テンプレートの
既定値でも、そのまま本番に導入できる既定構成でもありません。名前を省略するとクラスタ全体の既定値になります。

```csharp
using Orleans.Hosting;

#pragma warning disable ORLEANSEXP003
siloBuilder.AddDistributedGrainDirectory(); // 試験的機能。クラスタ全体の既定値
#pragma warning restore ORLEANSEXP003
```

別の方法として、名前付きディレクトリを登録し、Grain の実装クラスに
`[GrainDirectory("ConsistencyDirectory")]` を付けて選択できます。

```csharp
#pragma warning disable ORLEANSEXP003
siloBuilder.AddDistributedGrainDirectory("ConsistencyDirectory");
#pragma warning restore ORLEANSEXP003
```

属性には `Orleans.GrainDirectory` 名前空間を使います。インターフェースではなく Grain クラスに付けます。
Sekiban の組み込み Grain に型ごとの設定を適用するには、実装クラスの変更が必要です。ホストで名前付きディレクトリを登録するだけでは、それらの Grain は選択しません。
[10.3.1 の登録 API ソース](https://github.com/dotnet/orleans/blob/v10.3.1/src/Orleans.Runtime/Hosting/CoreHostingExtensions.cs)を参照してください。
異常終了後の復旧には範囲リース（`RangeLeaseDuration`、30秒）が使われるため、この復旧待ち時間を考慮してください。

### 外部ディレクトリと残る保証境界

Redis、Azure Table、ADO.NET のディレクトリは、それぞれ独自の整合性を持ちます。型ごと、または既定値として
登録できます（例: `UseRedisGrainDirectoryAsDefault`、`UseAzureTableGrainDirectoryAsDefault`。対応するプロバイダの
パッケージとオプションが必要です）。同じ正しさの保証を提供するわけではありません。Azure Table の実装には登録時の
競合があり、Redis の `EntryExpiry` は重複活性化を引き起こし得ます。

どのディレクトリも、死亡判定済みでも動作を続けているサイロをフェンスできません。Orleans がプロセスを終了するのは、
そのサイロが自身の死亡状態を認識したときです。それまでは、同居する API、キャッシュされた経路、処理中の呼び出しを
通じて活性化が要求を処理し続ける場合があります。強整合ディレクトリは競合の範囲を狭めますが、最終的なフェンスは
ストレージ側の条件付き検査です。[Orleans のクラスタ管理](https://learn.microsoft.com/en-us/dotnet/orleans/implementation/cluster-management)を参照してください。

- ディレクトリ構成を混在させるローリング更新ではなく、クラスタ全体を切り替えてください。ローリング切り替えの手順は
  文書化されていません。試験的ディレクトリを採用する前に、展開とロールバックの計画を準備し、検証してください。
- メンバーシップと障害検出を環境に合わせて調整し、迅速な検出と誤検知のバランスを取ってください。古いサイロを終了し、
  メンバーシップの変動を監視して、ホスト基盤が終了したプロセスを再起動できるようにしてください。
- 厳密な保証にはストレージのフェンスを使ってください。[予約の保証境界](03_aggregate_command_events.md)で G15/G16 の
  ユニーク追記と PostgreSQL のオプトイン `ExpectedTagPositions` を説明しています。通常の書き込みにはフェンスがありません。

### 重複活性化時の Sekiban 各機能のリスク

| 機能 | 残るリスクと保護 |
|------|----------------|
| `TagConsistentGrain` / `GeneralTagConsistentActor` | 正しさに関わるリスク。予約とキャッシュ済みタグ先頭は活性化ごとに独立しており、2つの活性化が同じ期待先頭で予約し、既定の書き込み経路で両方とも追記し得ます。 PostgreSQL の [derived fence](11_storage_providers.md#derived-fence-tagconsistencyfenceoptions) を有効にすると、読み取った予約入力を活性化間でも耐久的に比較します。未読タグはフェンスされません。 |
| `MultiProjectionGrain` のチェックポイント | CAS 対応ストア（InMemory、SQLite、DynamoDB、PostgreSQL、Cosmos）では [SEK-G20 generation-aware checkpoint CAS](11_storage_providers.md#sek-g20-generation-aware-checkpoint-cas) が保護します。イベント ID による重複排除がリプレイを保護し、チェックポイントの採用後に追加の catch-up が必要になる場合があります。無条件書き込みを行うカスタムストアには、この CAS 保護がありません。 |
| `MaterializedViewGrain` | 重複活性化や再配信により、非冪等な SQL が二重適用され得ます。[冪等なプロジェクター SQL](20_materialized_view.md#順序保証と冪等性)を使ってください。活性化間のレジストリ位置・状態の競合は残り、レジストリのガードは別の課題（SEK-G103）です。 |
| `TagStateGrain`、ストリーム・イベント配信 | 内部キャッシュとリプレイの正しさは、ETag による Grain ストレージとイベント ID の重複排除で自己修復されます。少なくとも1回の配信では、任意のコンシューマーの副作用にも冪等性が必要です。 |

## Orleans なしでのテスト

`InMemorySekibanExecutor` を使えばサイロ無しでもコマンド処理を試せます。

## Orleans でのオプション executor サイズゲート

厳密な永続化前の予算を使う場合は、`OrleansDcbExecutor` を解決する前に `AddSekibanDcbExecutorSizeGate` を登録します。
Orleans publisher は resolver の destination plan と service identity を一度キャプチャし、enqueue に同じ plan を再利用するため、
strict の destination policy はその capability が利用できる場合だけ成立します。利用できない、または identity が一致しない
destination policy を non-strict で使う場合は明示的な診断を伴って書き込みます。このゲートが測定するのは executor が生成した
logical event または宣言済み provider capability であり、Azure Queue の wire envelope・batch・retry の上限は主張せず、Orleans の
retry 動作も変更しません。

## DCB Orleans のバージョン権威とクラスタ全体のアップグレード

DCB 製品ラインの `Microsoft.Orleans.*` は **10.3.1** に揃えます。DCB のソース、内部ホスト、テスト、生成
テンプレートにある直接参照は、DCB 共通の権威ファイル（テンプレートでは各テンプレート固有の権威）で管理し、
各ホストでは同じ安定した Orleans 10.x のバージョンを使ってください。`Microsoft.Orleans.*` 10.3.1 は
`net8.0` と `net10.0` のアセットグループだけを公開します。net9 は `net8.0` グループを解決し、適用される
`Microsoft.Extensions.*` の下限は 8.0.x（例: `Microsoft.Extensions.Hosting` 8.0.1）です。net10 は `net10.0`
グループを解決し、下限は 10.0.5 です。`Polly` 8.6.4 と `Polly.Extensions` 8.6.5 は両グループの推移依存です。

アップグレードは Orleans クラスタ全体を同時に行ってください。10.0.1 と 10.3.1 を混在させるローリング
運用は、サポートまたは検証済みの互換性保証ではありません。この変更はランタイム/パッケージの入力を揃える
だけで、データやスキーマの移行は行わず、Azure Queue を巻き戻し可能にもせず、別スコープの #1185
subscriber-recovery を実装しません。Pure と Samples は従来の依存ラインを維持します。
