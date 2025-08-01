# Sekiban DevContainer Setup

このDevContainerは以下の環境を提供します：

## 🛠️ 含まれているツール

- **.NET 8.0 SDK** - Sekibanのバックエンド開発用
- **Node.js 20 + pnpm** - TypeScriptプロジェクト用
- **Git & GitHub CLI** - バージョン管理
- **Docker-in-Docker** - コンテナビルド
- **VS Code拡張機能**:
  - C# Dev Kit
  - TypeScript
  - GitHub Copilot
  - Claude Code

## 🚀 使用方法

1. VS Codeで「Dev Containers: Reopen in Container」を実行
2. 初回セットアップが自動実行されます
3. 開発環境が準備完了！

## 📁 ワークスペース構造

- `/workspace` - プロジェクトルート
- `/workspace/src` - .NETプロジェクト
- `/workspace/ts` - TypeScriptプロジェクト

## 🌐 ポート転送

- `5000` - .NET API (HTTP)
- `5001` - .NET API (HTTPS)  
- `3000` - Node.js開発サーバー
- `8080` - 追加サービス用

## 🔧 開発コマンド

### .NET

```bash
dotnet build
dotnet test
dotnet run
```

### TypeScript

```bash
cd ts
pnpm install
pnpm build
pnpm test
```

## 🤖 AI開発支援

- **GitHub Copilot** - コード補完とチャット
- **Claude Code** - 高度なコード分析と生成

環境が正常に動作しない場合は、コンテナを再ビルドしてください。
