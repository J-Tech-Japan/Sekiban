# API実装 - Sekiban イベントソーシング

> **ナビゲーション**
> - [コア概念](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [アグリゲート、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
> - [複数アグリゲートプロジェクター](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [ワークフロー](06_workflow.md)
> - [JSONとOrleansシリアライゼーション](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md) (現在位置)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md)
> - [Orleans設定](10_orleans_setup.md)
> - [Dapr設定](11_dapr_setup.md)
> - [ユニットテスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイメントガイド](16_deployment.md)

## API実装

### 基本的なセットアップパターン

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Core.Command;
using Sekiban.Core.Query;
using Sekiban.Infrastructure.Helpers;
using YourProject.Domain;
using YourProject.Domain.Generated;

// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 1. Orleansの設定
builder.UseOrleans(config =>
{
    // 独自のOrleans設定を行う
});

// 2. ドメインの登録
builder.Services.AddSingleton(
    YourProjectDomainDomainTypes.Generate(
        YourProjectDomainEventsJsonContext.Default.Options));

// 3. データベースの設定
builder.AddSekibanCosmosDb();  // または AddSekibanPostgresDb();

// 4. エンドポイントのマッピング
var app = builder.Build();
var apiRoute = app.MapGroup("/api");

// コマンドエンドポイントパターン
apiRoute.MapPost("/command",
    async ([FromBody] YourCommand command, 
           [FromServices] SekibanOrleansExecutor executor) => 
        await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox());

// クエリエンドポイントパターン
apiRoute.MapGet("/query",
    async ([FromServices] SekibanOrleansExecutor executor) =>
    {
        var result = await executor.QueryAsync(new YourQuery())
                                  .UnwrapBox();
        return result.Items;
    });
```

### 実装ステップ

1. `IAggregatePayload` を実装する集約を定義する
2. `IEventPayload` を実装するイベントを作成する
3. `IAggregateProjector` でプロジェクターを実装する
4. `ICommandWithHandler<TCommand, TProjector>` でコマンドを作成する
5. 適切なクエリインターフェースでクエリを定義する
6. JSONシリアライゼーションコンテキストを設定する
7. 上記のパターンを使用してProgram.csを設定する
8. コマンドとクエリのエンドポイントをマッピングする

### 効率的なAPIエンドポイントのためのToSimpleCommandResponse()の使用

コマンドを実行するAPIエンドポイントを作成する際、`ToSimpleCommandResponse()`拡張メソッドを使用すると次のような利点があります：

1. **ペイロードサイズの削減**：完全なCommandResponse（すべてのイベントを含む）をコンパクトなCommandResponseSimpleに変換します
2. **LastSortableUniqueIdへの簡単なアクセス**：クライアント側の一貫性のため最も重要な情報を抽出します
3. **クリーンなAPI設計**：`UnwrapBox()`と組み合わせることで、クリーンで一貫性のあるAPI応答を作成します

#### 実装例

```csharp
apiRoute
    .MapPost(
        "/inputweatherforecast",
        async (
                [FromBody] InputWeatherForecastCommand command,
                [FromServices] SekibanOrleansExecutor executor) =>
            await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox())
    .WithName("InputWeatherForecast")
    .WithOpenApi();
```

#### クライアント側の使用法

コマンドを実行した後、返される`LastSortableUniqueId`を使用して、後続のクエリが更新された状態を確認できるようにします：

```csharp
// コマンドを実行
var response = await weatherApiClient.InputWeatherAsync(new InputWeatherForecastCommand(...));

// 後続のクエリでLastSortableUniqueIdを使用
var forecasts = await weatherApiClient.GetWeatherAsync(
    waitForSortableUniqueId: response.LastSortableUniqueId);
```

このパターンにより、UIが常に最新の状態変更を反映し、より一貫性のあるユーザーエクスペリエンスを提供します。

### APIエンドポイントの整理

より大規模なアプリケーションでは、APIエンドポイントを別々のファイルに整理するとよいでしょう：

```csharp
// UserEndpoints.cs
namespace YourProject.Api.Endpoints;

public static class UserEndpoints
{
    public static WebApplication MapUserEndpoints(this WebApplication app)
    {
        var apiGroup = app.MapGroup("/api/users").WithTags("Users");
        
        // ユーザー登録
        apiGroup.MapPost("/register",
            async ([FromBody] RegisterUserCommand command, [FromServices] SekibanOrleansExecutor executor) =>
                await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox())
            .WithName("RegisterUser")
            .WithOpenApi();
            
        // ユーザー詳細の取得
        apiGroup.MapGet("/{userId}",
            async (Guid userId, [FromServices] SekibanOrleansExecutor executor) =>
            {
                var result = await executor.QueryAsync(new GetUserDetailsQuery(userId)).UnwrapBox();
                return result is null ? Results.NotFound() : Results.Ok(result);
            })
            .WithName("GetUserDetails")
            .WithOpenApi();
            
        return app;
    }
}
```

### 設定

```json
{
  "Sekiban": {
    "Database": "Cosmos"  // または "Postgres"
  }
}
```

より具体的なデータベース設定：

```json
{
  "Sekiban": {
    "Database": "Cosmos",
    "Cosmos": {
      "ConnectionString": "your-connection-string",
      "DatabaseName": "your-database-name"
    }
  }
}
```