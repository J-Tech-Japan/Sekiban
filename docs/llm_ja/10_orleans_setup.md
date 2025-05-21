# Orleansセットアップ - Sekiban イベントソーシング

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [集約、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
> - [複数集約プロジェクター](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [ワークフロー](06_workflow.md)
> - [JSONとOrleansのシリアライゼーション](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md)
> - [Orleansセットアップ](10_orleans_setup.md) (現在のページ)
> - [ユニットテスト](11_unit_testing.md)
> - [一般的な問題と解決策](12_common_issues.md)
> - [ResultBox](13_result_box.md)

## Orleansセットアップ

Microsoft Orleansは分散型の高スケールコンピューティングアプリケーションを構築するためのストレートフォワードなアプローチを提供するフレームワークです。Sekibanは堅牢でスケーラブルなイベントソーシングインフラストラクチャを提供するためにOrleansと統合されています。

## 基本的なOrleans設定

### Program.csでの設定

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Orleansの設定
builder.UseOrleans(siloBuilder =>
{
    // 開発用にlocalhostクラスタリングを使用
    siloBuilder.UseLocalhostClustering();
    
    // メモリグレインストレージを追加
    siloBuilder.AddMemoryGrainStorage("PubSubStore");
    
    // シリアライゼーションの設定
    siloBuilder.Services.AddSingleton<IGrainStorageSerializer, NewtonsoftJsonSekibanOrleansSerializer>();
});

// ドメインタイプの登録
builder.Services.AddSingleton(
    YourProjectDomainDomainTypes.Generate(
        YourProjectDomainEventsJsonContext.Default.Options));

// データベースの設定
builder.AddSekibanCosmosDb();  // または AddSekibanPostgresDb();

var app = builder.Build();
// アプリの残りの設定
```

## Orleansクラスタリングオプション

### 開発：ローカルクラスタリング

開発とテストのために、localhostクラスタリングを使用します：

```csharp
siloBuilder.UseLocalhostClustering();
```

### 本番環境：Azure Table Storageクラスタリング

Azure環境での本番用：

```csharp
siloBuilder.UseAzureStorageClustering(options =>
{
    options.ConfigureTableServiceClient(builder.Configuration["Orleans:StorageConnectionString"]);
});
```

### 本番環境：Kubernetesクラスタリング

Kubernetes環境の場合：

```csharp
siloBuilder.UseKubernetesHosting();
```

## 永続化グレインストレージ

Orleansはそのグレイン用のストレージプロバイダーを必要とします。いくつかの選択肢があります：

### メモリストレージ（開発用のみ）

```csharp
siloBuilder.AddMemoryGrainStorage("PubSubStore");
```

### Azure Blob Storage

```csharp
siloBuilder.AddAzureBlobGrainStorage(
    "PubSubStore", 
    options => options.ConfigureBlobServiceClient(
        builder.Configuration["Orleans:BlobConnectionString"]));
```

### カスタムストレージ

```csharp
siloBuilder.AddGrainStorage("PubSubStore", (sp, name) => 
    ActivatorUtilities.CreateInstance<YourCustomStorageProvider>(sp, name, 
        sp.GetRequiredService<IOptions<YourCustomStorageOptions>>()));
```

## SekibanとAspire

SekibanはシームレスにDOT NET Aspireアプリケーションホスティングモデルと連携します：

### AppHostプロジェクトの設定

```csharp
// AppHostプロジェクトのProgram.cs
var builder = DistributedApplication.CreateBuilder(args);

// ServiceDefaultsプロジェクトからのサービスデフォルトを追加
var defaultBuilder = builder.AddProject<Projects.ServiceDefaults>("servicedefaults");

// APIサービスを追加
var api = builder.AddProject<Projects.ApiService>("apiservice");

// Webフロントエンドを追加
var web = builder.AddProject<Projects.Web>("web");

// WebフロントエンドをAPIサービスに接続
web.WithReference(api);

// 必要に応じて追加のサービスを追加
var postgres = builder.AddPostgres("postgres");
api.WithReference(postgres);

// アプリケーションをビルドして実行
await builder.BuildApplication().RunAsync();
```

### ServiceDefaultsプロジェクトの設定

```csharp
// ServiceDefaultsプロジェクトのServiceDefaultsExtensions.cs
public static class ServiceDefaultsExtensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        // ヘルスチェックを追加
        builder.AddDefaultHealthChecks();

        // サービス検出を追加
        builder.Services.AddServiceDiscovery();

        // 分散構成でOrleansを追加
        builder.UseOrleans(siloBuilder =>
        {
            // 開発環境ではlocalhostクラスタリングを使用
            if (builder.Environment.IsDevelopment())
            {
                siloBuilder.UseLocalhostClustering();
            }
            else
            {
                // 本番環境ではAzureクラスタリングまたは他の本番向きクラスタリング方法を使用
                siloBuilder.UseAzureStorageClustering(options =>
                {
                    options.ConfigureTableServiceClient(
                        builder.Configuration.GetConnectionString("OrleansStorage"));
                });
            }

            // 共通のグレインストレージプロバイダーを追加
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            
            // シリアライゼーションを設定
            siloBuilder.Services.AddSingleton<IGrainStorageSerializer, NewtonsoftJsonSekibanOrleansSerializer>();
        });

        return builder;
    }
}
```

### APIサービスプロジェクトの設定

```csharp
// APIServiceプロジェクトのProgram.cs
var builder = WebApplication.CreateBuilder(args);

// ServiceDefaultsプロジェクトからのサービスデフォルトを追加
builder.AddServiceDefaults();

// ドメインタイプを登録
builder.Services.AddSingleton(
    YourDomainDomainTypes.Generate(
        YourDomainEventsJsonContext.Default.Options));

// Sekibanデータベースを設定
if (builder.Configuration["Sekiban:Database"] == "Cosmos")
{
    builder.AddSekibanCosmosDb();
}
else
{
    builder.AddSekibanPostgresDb();
}

// APIサービスのその他の設定...
```

## Orleansダッシュボード

監視と管理のために、Orleansダッシュボードを追加できます：

```csharp
// 監視用のOrleansダッシュボードを追加
siloBuilder.UseDashboard(options =>
{
    options.Port = 8081;
    options.HideTrace = true;
    options.CounterUpdateIntervalMs = 10000;
});
```

その後、`http://localhost:8081`でダッシュボードにアクセスできます。

## Orleansクライアント設定

Orleansクラスタに接続する必要がある別のクライアントアプリケーションがある場合：

```csharp
// Orleansクライアントの設定
builder.UseOrleansClient(clientBuilder =>
{
    // 開発環境では、localhostに接続
    if (builder.Environment.IsDevelopment())
    {
        clientBuilder.UseLocalhostClustering();
    }
    else
    {
        // 本番環境では、Azureクラスタリングまたは他の本番用クラスタリングに接続
        clientBuilder.UseAzureStorageClustering(options =>
        {
            options.ConfigureTableServiceClient(
                builder.Configuration.GetConnectionString("OrleansStorage"));
        });
    }
});
```

## デプロイメント

Sekibanはローカル開発、ステージング、本番環境など、様々な環境へのデプロイメントをサポートしています。

### ローカル開発

localhostクラスタリングを使用したデフォルトのテンプレート設定を使用します。

### Azure Container Apps

1. コンテナレジストリを作成し、イメージをプッシュする
2. Azure Container Appの環境を作成する
3. サービスをContainer Appsとしてデプロイする
4. クラスタリングストレージ（例：Azure Storage Tables）が設定されていることを確認する

Azure CLIコマンドの例：

```bash
# リソースグループの作成
az group create --name your-resource-group --location eastus

# Container App環境の作成
az containerapp env create --name your-env --resource-group your-resource-group --location eastus

# Orleansクラスタリング用のAzure Storageアカウントの作成
az storage account create --name yourstorageaccount --resource-group your-resource-group --location eastus --sku Standard_LRS

# コンテナアプリのデプロイ
az containerapp create \
  --name api-service \
  --resource-group your-resource-group \
  --environment your-env \
  --image your-registry.azurecr.io/apiservice:latest \
  --target-port 80 \
  --ingress external \
  --min-replicas 1 \
  --max-replicas 10 \
  --secrets "ORLEANS_STORAGE=storage-connection-string" \
  --env-vars "Orleans__StorageConnectionString=secretref:ORLEANS_STORAGE"
```

### Kubernetes

1. Kubernetesクラスタを作成する
2. KubernetesマニフェストまたはHelmチャートを使用してサービスをデプロイする
3. KubernetesクラスタリングのためにOrleansを設定する

YAMLファイルの例：

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api-service
spec:
  replicas: 3
  selector:
    matchLabels:
      app: api-service
  template:
    metadata:
      labels:
        app: api-service
    spec:
      containers:
      - name: api-service
        image: your-registry.azurecr.io/apiservice:latest
        ports:
        - containerPort: 80
        env:
        - name: ORLEANS__USEKUBERNETESHOSTING
          value: "true"
        - name: SEKIBAN__DATABASE
          value: "Postgres"
        - name: SEKIBAN__POSTGRES__CONNECTIONSTRING
          valueFrom:
            secretKeyRef:
              name: postgres-secrets
              key: connection-string
```

## スケーリングに関する考慮事項

Orleansは水平スケーリングを念頭に設計されていますが、以下の点に注意してください：

1. **ワークロード分散**：集約が適切にパーティション分割されていることを確認する
2. **ストレージパフォーマンス**：パフォーマンスニーズに基づいて適切なストレージプロバイダーを選択する
3. **グレインアクティベーション**：グレインのアクティベーションとディアクティベーションのパターンを監視する
4. **リソース割り当て**：OrleansサービスのためにCPUとメモリを適切に割り当てる