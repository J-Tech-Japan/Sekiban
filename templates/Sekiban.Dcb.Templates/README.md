# Sekiban Dcb Templates

Sekiban Dcb (Distributed Consistent Bus) 向けの .NET Aspire + Orleans スターターテンプレートを提供します。

## インストール

```bash
dotnet new install Sekiban.Dcb.Templates
```

## 利用可能なテンプレート

### 1. Dcb Orleans Aspire テンプレート (WithResult)

```bash
dotnet new sekiban-dcb-orleans -n YourProjectName
```

含まれるもの:

- .NET 10.0 + Aspire 9.4 AppHost
- Orleans クラスタ (Azure Storage / Queue / Tables / Blobs)
- PostgreSQL (PgAdmin 付き) + マイグレーション基本形
- Sekiban Dcb Orleans/Postgres 連携 (WithResult)
- Web フロント (Blazor Server) + API Service
- ServiceDefaults (共通設定) / Unit Test プロジェクト

### 2. Dcb Orleans Aspire テンプレート (WithoutResult)

```bash
dotnet new sekiban-dcb-orleans-withoutresult -n YourProjectName
```

含まれるもの:

- .NET 10.0 + Aspire 9.4 AppHost
- Orleans クラスタ (Azure Storage / Queue / Tables / Blobs)
- PostgreSQL (PgAdmin 付き) + マイグレーション基本形
- Sekiban Dcb Orleans/Postgres 連携 (WithoutResult)
- Web フロント (Blazor Server) + API Service
- ServiceDefaults (共通設定) / Unit Test プロジェクト

## 生成後の手順

```bash
dotnet restore
```

AppHost 実行:

```bash
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
