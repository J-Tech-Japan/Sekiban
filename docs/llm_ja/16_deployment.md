# デプロイメントガイド - Sekiban イベントソーシング

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
> - [Orleansセットアップ](10_orleans_setup.md)
> - [Daprセットアップ](11_dapr_setup.md)
> - [ユニットテスト](12_unit_testing.md)
> - [一般的な問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [値オブジェクト](15_value_object.md)
> - [デプロイメントガイド](16_deployment.md) (現在のページ)

## デプロイメントガイド

このガイドでは、提供されたBicepテンプレートを使用したSekibanアプリケーションのデプロイメントオプションについて説明します。現在、OrleansとDapr実装の両方でAzureデプロイメントがサポートされています。

## 前提条件

### Azure CLI セットアップ

1. **Azureにログイン**: まず、対象のAzureテナントにログインします：

```bash
# テナントIDを指定してログイン
az login --tenant <tenant-id>

# または組織のドメイン名を使用してログイン
az login --tenant contoso.onmicrosoft.com

# 同じユーザー名で複数のアカウントが存在する場合は、デバイスコードを使用
az login --tenant <tenant-id> --use-device-code
```

2. **必要なリソースプロバイダーを登録**: 各デプロイメントオプションには特定のAzureリソースプロバイダーが必要です。

## デプロイメントオプション

Sekibanは、異なるAzureデプロイメントシナリオ用に事前設定されたBicepテンプレートを提供します：

### Orleansベースのデプロイメント

#### 1. Azure App Service (フル機能版)
**場所**: `templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/infrastructure/azure_appservice/`

**適用場面**: App Serviceの全機能、SSL証明書、カスタムドメイン、統合監視が必要な本番アプリケーション。

**機能**:
- スケーリング機能付きAzure App Service
- Azure SQL DatabaseまたはCosmos DB
- Application Insights統合
- SSL証明書とカスタムドメイン
- ブルー・グリーンデプロイメント用のステージングスロット

#### 2. Azure App Service (最小版)
**場所**: `templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/infrastructure/azure_appservice_minimal/`

**適用場面**: 開発、テスト、またはコスト最適化された本番デプロイメント。

**機能**:
- 基本的なAzure App Service
- 最小限のリソース構成
- 基本的な監視
- コスト最適化されたセットアップ

#### 3. Azure Container Apps (Orleans)
**場所**: `templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/infrastructure/azure_container_apps/`

**適用場面**: 高度なスケーリングとマイクロサービスアーキテクチャを必要とするコンテナ化されたデプロイメント。

**機能**:
- Container Apps環境
- 需要に基づく自動スケーリング
- KEDAベースのスケーリングトリガー
- 統合されたLog Analytics
- サービスディスカバリー

### Daprベースのデプロイメント

#### 4. Azure Container Apps (Dapr)
**場所**: `templates/Sekiban.Pure.Templates/content/Sekiban.Dapr.Aspire/infrastructure/azure_container_apps/`

**適用場面**: Daprのサイドカーパターンを活用したクラウドネイティブスケーリングのマイクロサービスアプリケーション。

**機能**:
- サイドカーパターンでのDapr統合
- パブ・サブメッセージング用Service Bus
- Daprコンポーネント付きContainer Apps
- 外部ストアでの状態管理
- 分散トレーシングと監視

## デプロイメント手順

### ステップ1: テンプレートを選択

アーキテクチャの選択に基づいて適切なテンプレートディレクトリに移動します：

```bash
# Orleans App Service用
cd templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/infrastructure/azure_appservice/

# Orleans Container Apps用
cd templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/infrastructure/azure_container_apps/

# Orleans最小版App Service用
cd templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/infrastructure/azure_appservice_minimal/

# Dapr Container Apps用
cd templates/Sekiban.Pure.Templates/content/Sekiban.Dapr.Aspire/infrastructure/azure_container_apps/
```

### ステップ2: デプロイメント設定を構成

各テンプレートディレクトリには特定の手順が記載された`how.md`ファイルが含まれています。一般的には以下が必要です：

1. **設定ファイルの作成**: デプロイメント設定ファイル（通常`mydeploy.local.json`）を作成します：

```json
{
    "resourceGroupName": "your-sekiban-app-123",
    "location": "japaneast",
    "backendRelativePath": "../../YourProject.ApiService",
    "frontendRelativePath": "../../YourProject.Web",
    "logincommand": "az login --tenant yourorg.onmicrosoft.com --use-device-code"
}
```

**重要**: リソースグループ名には小文字、数字、ハイフンのみを使用してください。

2. **リソースプロバイダーの登録**: プロバイダー登録スクリプトを実行します：

```bash
chmod +x ./register_providers.sh
./register_providers.sh
```

### ステップ3: インフラストラクチャをデプロイ

各テンプレートの`how.md`ファイルの特定のデプロイメント手順に従います。通常は以下のようになります：

```bash
# デプロイメントスクリプトを実行可能にする
chmod +x ./deploy.sh

# 設定を使用してデプロイメントを実行
./deploy.sh mydeploy
```

## テンプレート固有の機能

### Orleans App Serviceテンプレート

**必要なリソースプロバイダー**:
- `Microsoft.Web` (App Service)
- `Microsoft.Sql` (Azure SQL Database)
- `Microsoft.DocumentDB` (Cosmos DB)
- `Microsoft.Insights` (Application Insights)

**主要コンポーネント**:
- 自動スケーリング付きApp Service Plan
- Azure SQL DatabaseまたはCosmos DB
- 監視用Application Insights
- シークレット管理用Key Vault

### Orleans Container Appsテンプレート

**必要なリソースプロバイダー**:
- `Microsoft.App` (Container Apps)
- `Microsoft.ContainerService` (Container Apps Environment)
- `Microsoft.OperationalInsights` (Log Analytics)
- `Microsoft.DocumentDB` (Cosmos DB)

**主要コンポーネント**:
- Container Apps環境
- Orleans設定付きContainer Apps
- Log Analyticsワークスペース
- Application Insights
- Cosmos DBまたはPostgreSQL

### Dapr Container Appsテンプレート

**必要なリソースプロバイダー**:
- `Microsoft.App` (Container Apps)
- `Microsoft.ContainerService` (Container Apps Environment)
- `Microsoft.OperationalInsights` (Log Analytics)
- `Microsoft.ServiceBus` (Daprパブ・サブ用Service Bus)

**主要コンポーネント**:
- Dapr付きContainer Apps環境
- Daprコンポーネント（状態ストア、パブ・サブ）
- メッセージング用Service Bus
- 状態管理用Redis Cache
- Log Analyticsワークスペース

## 環境設定

### 開発 vs 本番

テンプレートは環境固有の設定をサポートします：

```json
{
    "environment": "development",
    "resourceGroupName": "sekiban-dev-123",
    "scaling": {
        "minReplicas": 1,
        "maxReplicas": 3
    }
}
```

```json
{
    "environment": "production", 
    "resourceGroupName": "sekiban-prod-456",
    "scaling": {
        "minReplicas": 3,
        "maxReplicas": 30
    }
}
```

### データベース設定

テンプレートは複数のデータベースオプションをサポートします：

**Azure SQL Database** (Orleans):
```json
{
    "database": {
        "type": "sql",
        "sku": "S2",
        "backupRetention": 7
    }
}
```

**Cosmos DB** (Orleans/Dapr):
```json
{
    "database": {
        "type": "cosmos",
        "consistencyLevel": "Session",
        "throughput": 400
    }
}
```

**PostgreSQL** (Dapr):
```json
{
    "database": {
        "type": "postgresql",
        "sku": "B_Gen5_1",
        "storage": "5120"
    }
}
```

## 監視と可観測性

すべてのテンプレートには監視機能が含まれています：

### Application Insights
- リクエスト追跡
- 依存関係監視
- 例外ログ
- カスタムメトリクス

### Log Analytics
- コンテナログ
- アプリケーションログ
- パフォーマンスメトリクス
- クエリ機能

### ヘルスチェック
- Readinessプローブ
- Livenessプローブ
- Startupプローブ

## セキュリティ考慮事項

### Key Vault統合
テンプレートには安全なシークレット管理のためのAzure Key Vaultが含まれています：

```json
{
    "keyVault": {
        "name": "sekiban-kv-123",
        "secrets": [
            "ConnectionStrings--DefaultConnection",
            "ApplicationInsights--InstrumentationKey"
        ]
    }
}
```

### マネージドID
テンプレートは安全なAzureサービス認証のためにマネージドIDを使用します：

```bicep
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
    name: 'sekiban-identity'
    location: location
}
```

### ネットワークセキュリティ
- データベース用プライベートエンドポイント
- 仮想ネットワーク統合
- ネットワークセキュリティグループ
- SSL終端用Application Gateway

## スケーリング設定

### Orleansスケーリング
```json
{
    "orleans": {
        "clustering": {
            "providerId": "AzureTable",
            "deploymentId": "sekiban-prod"
        },
        "silos": {
            "min": 2,
            "max": 10
        }
    }
}
```

### Daprスケーリング
```json
{
    "dapr": {
        "components": {
            "stateStore": "redis",
            "pubSub": "servicebus"
        },
        "scaling": {
            "triggers": ["cpu", "memory", "queue-length"]
        }
    }
}
```

## CI/CD統合

テンプレートはAzure DevOpsとGitHub Actionsとの連携を想定して設計されています：

### Azure DevOpsパイプライン
```yaml
- task: AzureCLI@2
  displayName: 'Deploy Infrastructure'
  inputs:
    azureSubscription: '$(azureSubscription)'
    scriptType: 'bash'
    scriptLocation: 'scriptPath'
    scriptPath: './infrastructure/deploy.sh'
    arguments: '$(deploymentConfig)'
```

### GitHub Actions
```yaml
- name: Deploy to Azure
  uses: azure/CLI@v1
  with:
    azcliversion: 2.50.0
    inlineScript: |
      cd ./infrastructure/azure_container_apps
      chmod +x ./deploy.sh
      ./deploy.sh ${{ secrets.DEPLOYMENT_CONFIG }}
```

## トラブルシューティング

### よくある問題

1. **リソースプロバイダーが登録されていない**:
   ```bash
   az provider register --namespace Microsoft.App
   ```

2. **権限不足**:
   - サブスクリプションでContributor権限があることを確認
   - リソースグループの権限を確認

3. **テンプレート検証エラー**:
   ```bash
   az deployment group validate --resource-group myRG --template-file main.bicep
   ```

4. **コンテナイメージの問題**:
   - コンテナレジストリアクセスを確認
   - イメージタグとバージョンを確認

### デプロイメントのデバッグ

```bash
# デプロイメント状況を確認
az deployment group show --resource-group myRG --name myDeployment

# デプロイメントログを表示
az monitor activity-log list --resource-group myRG

# コンテナログを確認
az containerapp logs show --name myapp --resource-group myRG
```

## 将来のデプロイメントオプション

以下のデプロイメントオプションが将来のリリースで計画されています：

### オンプレミスデプロイメント
- Docker Composeテンプレート
- Kubernetesマニフェスト
- Helmチャート

### AWSデプロイメント
- CloudFormationテンプレート
- ECS/Fargateデプロイメント
- EKSクラスターセットアップ
- Lambdaサーバーレスオプション

### Google Cloud Platform
- Cloud Runデプロイメント
- GKEクラスターセットアップ
- Cloud Functions統合

## ベストプラクティス

1. **環境分離**: 異なる環境には別々のリソースグループを使用
2. **リソース命名**: 一貫した命名規則に従う
3. **コスト管理**: 各環境に適切なSKUを使用
4. **セキュリティ**: 常にマネージドIDとKey Vaultを使用
5. **監視**: 包括的なログと監視を有効化
6. **バックアップ**: 適切なバックアップ戦略を設定
7. **スケーリング**: 実際の使用パターンに基づいて自動スケーリングを設定

## 次のステップ

デプロイメント後：
1. 監視ダッシュボードを設定
2. アラートと通知を設定
3. バックアップと災害復旧を実装
4. 容量スケーリングを計画
5. CI/CDパイプラインを設定
6. セキュリティポリシーを設定

具体的なデプロイメント手順については、選択したテンプレートディレクトリ内の`how.md`ファイルを常に参照してください。
