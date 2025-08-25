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

## Azure OpenAI

このテンプレートは、Azure OpenAIリソースを自動的に作成し、以下のモデルをデプロイします：

### 自動デプロイされるモデル

- **GPT-4.1**: `gpt-4` (turbo-2024-04-09版) - 最新のGPT-4.1機能を提供
- **Text Embedding**: `text-embedding-ada-002`

### Key Vaultシークレット

Azure OpenAIの設定は自動的にKey Vaultに保存されます：

- `AzureOpenAIEndpoint`: Azure OpenAIのエンドポイント（自動設定）
- `AzureOpenAIApiKey`: Azure OpenAIのAPIキー（自動設定）
- `AzureOpenAIDeploymentName`: GPT-4.1のデプロイ名（自動設定）
- `AzureOpenAIEmbeddingDeploymentName`: 埋め込みモデルのデプロイ名（自動設定）

### デプロイされるリソース

1. **Azure OpenAI アカウント**: `aoai-{resourceGroupName}`
2. **GPT-4.1 デプロイメント**: `gpt-41`
3. **Text Embedding デプロイメント**: `text-embedding-ada-002`
4. **Key Vault シークレット**: 上記の設定値が自動的に保存

### 注意事項

- Azure OpenAIは利用可能なリージョンが限られています
- GPT-4.1モデルは容量制限があるため、デプロイに時間がかかる場合があります
- 初回デプロイ時にAzure OpenAIサービスの利用申請が必要な場合があります
- GPT-4.1は `gpt-4` モデルの `turbo-2024-04-09` バージョンとしてデプロイされます

## デプロイ後の確認

デプロイが完了したら、以下を確認してください：

1. Azure ポータルでAzure OpenAIリソースが作成されていること
2. GPT-4.1とText Embeddingモデルがデプロイされていること
3. Key Vaultに必要なシークレットが保存されていること
4. App ServiceがKey Vaultのシークレットにアクセスできること

## トラブルシューティング

### Azure OpenAIのデプロイが失敗する場合

1. リージョンがAzure OpenAIをサポートしているか確認
2. Azure OpenAIサービスの利用申請が承認されているか確認
3. サブスクリプションの制限を確認
4. GPT-4.1モデル（turbo-2024-04-09）が選択したリージョンで利用可能か確認

### Key Vaultアクセスエラーの場合

1. App ServiceのManaged Identityが有効になっているか確認
2. Key Vaultのアクセスポリシーが正しく設定されているか確認

### GPT-4.1モデルについて

- Azure OpenAIでは、GPT-4.1の機能は `gpt-4` モデルの `turbo-2024-04-09` バージョンで提供されます
- このバージョンには、改善されたパフォーマンスと新機能が含まれています
- デプロイ名は `gpt-41` として設定されますが、実際のモデル名は `gpt-4` です
