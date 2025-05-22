# Sekiban Document MCP Azure App Service デプロイ

このディレクトリには、SekibanDocumentMcpSseをAzure App Serviceにデプロイするためのスクリプトと設定ファイルが含まれています。

## 必要条件

- Azure CLI
- .NET 8.0 SDK
- jq

## 使い方

### 1. 設定ファイルの準備

`sample.local.json`を参考にして、自分の環境に合わせた設定ファイル（例: `dev.local.json`）を作成します。

```json
{
  "resourceGroupName": "rg-sekiban-doc-mcp",
  "location": "japaneast",
  "mcpRelativePath": "../../"
}
```

### 2. リソースグループの作成

```bash
./create_resource_group.sh dev
```

### 3. インフラストラクチャのデプロイ

```bash
./runbicep.sh dev minimal_main.bicep
```

### 4. アプリケーションのデプロイ

```bash
./code_deploy_mcp.sh dev
```

## GitHub Actionsによる自動デプロイ

mainブランチへのプッシュ時に自動的にデプロイされるように設定されています。また、手動でデプロイすることもできます。

GitHub Secretsの設定:
- `AZURE_CREDENTIALS`: Azure Service Principalの認証情報

## 構成ファイル

- `minimal_main.bicep`: メインのBicepテンプレート
- 各サブディレクトリには特定のリソースタイプ用のBicepテンプレートが含まれています

## Key Vaultシークレット

デプロイ後、以下のシークレットをKey Vaultに追加する必要があります:

- `AzureOpenAIEndpoint`: Azure OpenAIのエンドポイント
- `AzureOpenAIApiKey`: Azure OpenAIのAPIキー
- `AzureOpenAIDeploymentName`: Azure OpenAIのデプロイ名
- `AzureOpenAIEmbeddingDeploymentName`: Azure OpenAIの埋め込みモデルデプロイ名