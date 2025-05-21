# クエリ - Sekiban イベントソーシング

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [集約、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
> - [複数集約プロジェクター](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md) (現在のページ)
> - [ワークフロー](06_workflow.md)
> - [JSONとOrleansのシリアライゼーション](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md)
> - [Orleansセットアップ](10_orleans_setup.md)
> - [ユニットテスト](11_unit_testing.md)
> - [一般的な問題と解決策](12_common_issues.md)

## クエリ（データ取得）

Sekibanはリストクエリと非リストクエリの2種類のクエリをサポートしています。

### リストクエリ

リストクエリはアイテムのコレクションを返し、フィルタリングとソート操作をサポートします。

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using Sekiban.Pure.ResultBoxes;
using System;
using System.Collections.Generic;
using System.Linq;

[GenerateSerializer]
public record YourListQuery(string FilterParameter = null)
    : IMultiProjectionListQuery<AggregateListProjector<YourAggregateProjector>, YourListQuery, YourListQuery.ResultRecord>
{
    public static ResultBox<IEnumerable<ResultRecord>> HandleFilter(
        MultiProjectionState<AggregateListProjector<YourAggregateProjector>> projection, 
        YourListQuery query, 
        IQueryContext context)
    {
        return projection.Payload.Aggregates
            .Where(m => m.Value.GetPayload() is YourAggregate)
            .Select(m => ((YourAggregate)m.Value.GetPayload(), m.Value.PartitionKeys))
            .Select(tuple => new ResultRecord(tuple.PartitionKeys.AggregateId, ...other properties...))
            .ToResultBox();
    }

    public static ResultBox<IEnumerable<ResultRecord>> HandleSort(
        IEnumerable<ResultRecord> filteredList, 
        YourListQuery query, 
        IQueryContext context)
    {
        return filteredList.OrderBy(m => m.SomeProperty).AsEnumerable().ToResultBox();
    }

    [GenerateSerializer]
    public record ResultRecord(Guid Id, ...other properties...);
}
```

**リストクエリに必要なもの**:
- `IMultiProjectionListQuery<TProjection, TQuery, TResult>` インターフェースの実装
- クエリパラメータに基づいてデータをフィルタリングする静的 `HandleFilter` メソッドの実装
- フィルタリングされた結果をソートする静的 `HandleSort` メソッドの実装
- `[GenerateSerializer]` 属性を持つネストされた結果レコードの定義

#### 例：ユーザーリストクエリ

```csharp
[GenerateSerializer]
public record ListUsersQuery(string? NameFilter = null, bool? ConfirmedOnly = null)
    : IMultiProjectionListQuery<AggregateListProjector<UserProjector>, ListUsersQuery, ListUsersQuery.ResultRecord>
{
    public static ResultBox<IEnumerable<ResultRecord>> HandleFilter(
        MultiProjectionState<AggregateListProjector<UserProjector>> projection, 
        ListUsersQuery query, 
        IQueryContext context)
    {
        var users = projection.Payload.Aggregates
            .Select(m => new { Key = m.Key, Payload = m.Value.GetPayload() })
            .Where(m => m.Payload is User || m.Payload is ConfirmedUser);
            
        // 名前フィルターが提供されている場合は適用
        if (!string.IsNullOrEmpty(query.NameFilter))
        {
            users = users.Where(m => 
                (m.Payload as User)?.Name?.Contains(query.NameFilter, StringComparison.OrdinalIgnoreCase) == true ||
                (m.Payload as ConfirmedUser)?.Name?.Contains(query.NameFilter, StringComparison.OrdinalIgnoreCase) == true);
        }
        
        // 確認フィルターが提供されている場合は適用
        if (query.ConfirmedOnly == true)
        {
            users = users.Where(m => m.Payload is ConfirmedUser);
        }
        
        return users.Select(m => {
            var isConfirmed = m.Payload is ConfirmedUser;
            var user = (m.Payload as User) ?? (m.Payload as ConfirmedUser);
            return new ResultRecord(
                m.Key,
                user!.Name,
                user!.Email,
                isConfirmed);
        }).ToResultBox();
    }

    public static ResultBox<IEnumerable<ResultRecord>> HandleSort(
        IEnumerable<ResultRecord> filteredList, 
        ListUsersQuery query, 
        IQueryContext context)
    {
        return filteredList.OrderBy(m => m.Name).ToResultBox();
    }

    [GenerateSerializer]
    public record ResultRecord(Guid Id, string Name, string Email, bool IsConfirmed);
}
```

### 非リストクエリ

非リストクエリは単一の結果を返し、通常は条件をチェックしたり特定の値を取得したりするために使用されます。

```csharp
[GenerateSerializer]
public record YourNonListQuery(string Parameter)
    : IMultiProjectionQuery<AggregateListProjector<YourAggregateProjector>, YourNonListQuery, bool>
{
    public static ResultBox<bool> HandleQuery(
        MultiProjectionState<AggregateListProjector<YourAggregateProjector>> projection,
        YourNonListQuery query,
        IQueryContext context)
    {
        return projection.Payload.Aggregates.Values
            .Any(aggregate => SomeCondition(aggregate, query.Parameter));
    }
    
    private static bool SomeCondition(Aggregate aggregate, string parameter)
    {
        // ここに条件ロジックを記述
        return aggregate.GetPayload() is YourAggregate payload && 
               payload.SomeProperty == parameter;
    }
}
```

**非リストクエリに必要なもの**:
- `IMultiProjectionQuery<TProjection, TQuery, TResult>` インターフェースの実装
- 単一の結果を返す静的 `HandleQuery` メソッドの実装
- 結果の型はシリアライズ可能な任意の型（bool、string、int、カスタムレコードなど）

#### 例：ユーザー詳細クエリ

```csharp
[GenerateSerializer]
public record GetUserDetailsQuery(Guid UserId)
    : IMultiProjectionQuery<AggregateListProjector<UserProjector>, GetUserDetailsQuery, GetUserDetailsQuery.UserDetails?>
{
    public static ResultBox<UserDetails?> HandleQuery(
        MultiProjectionState<AggregateListProjector<UserProjector>> projection,
        GetUserDetailsQuery query,
        IQueryContext context)
    {
        if (!projection.Payload.Aggregates.TryGetValue(query.UserId, out var aggregate))
        {
            return null;
        }
        
        var payload = aggregate.GetPayload();
        
        if (payload is User user)
        {
            return new UserDetails(
                query.UserId,
                user.Name,
                user.Email,
                isConfirmed: false,
                aggregate.Version,
                aggregate.LastModified);
        }
        
        if (payload is ConfirmedUser confirmedUser)
        {
            return new UserDetails(
                query.UserId,
                confirmedUser.Name,
                confirmedUser.Email,
                isConfirmed: true,
                aggregate.Version,
                aggregate.LastModified);
        }
        
        return null;
    }
    
    [GenerateSerializer]
    public record UserDetails(
        Guid Id,
        string Name,
        string Email,
        bool IsConfirmed,
        int Version,
        DateTime LastModified);
}
```

## 高度なクエリ機能

### IWaitForSortableUniqueIdを使用した特定イベントの待機

イベントソーシングでリアルタイムUIアプリケーションを構築する場合、コマンドの実行と更新された状態がクエリで利用可能になる間にはしばしば遅延があります。Sekibanは `IWaitForSortableUniqueId` インターフェースでこれを解決します。

```csharp
// 特定のイベントを待機できるクエリを定義
[GenerateSerializer]
public record YourQuery(string QueryParam) : 
    IMultiProjectionQuery<YourProjection, YourQuery, YourResult>,
    IWaitForSortableUniqueId
{
    // インターフェースのプロパティを実装
    public string? WaitForSortableUniqueId { get; set; }
    
    // クエリ処理の実装
    public static ResultBox<YourResult> HandleQuery(
        MultiProjectionState<YourProjection> state,
        YourQuery query,
        IQueryContext context)
    {
        // ここにクエリロジックを記述
    }
}
```

**待機可能クエリに必要なもの**:
- `IWaitForSortableUniqueId` インターフェースの実装
- getterとsetterを持つパブリックプロパティ `WaitForSortableUniqueId` の追加
- プロパティは null許容の string 型であるべき

#### 実装例：APIエンドポイント

```csharp
// Program.csで
apiRoute.MapGet("/your-endpoint",
    async ([FromQuery] string? waitForSortableUniqueId, [FromServices] SekibanOrleansExecutor executor) =>
    {
        var query = new YourQuery("parameter") 
        {
            WaitForSortableUniqueId = waitForSortableUniqueId
        };
        return await executor.QueryAsync(query).UnwrapBox();
    });
```

#### 実装例：クライアント側

```csharp
// APIクライアントの実装
public async Task<YourResult> GetResultAsync(string? waitForSortableUniqueId = null)
{
    var requestUri = string.IsNullOrEmpty(waitForSortableUniqueId)
        ? "/api/your-endpoint"
        : $"/api/your-endpoint?waitForSortableUniqueId={Uri.EscapeDataString(waitForSortableUniqueId)}";
    
    // HTTPリクエストの実行
}

// コマンド実行後にクライアントを使用
var commandResult = await client.ExecuteCommandAsync(new YourCommand());
var updatedResult = await client.GetResultAsync(commandResult.LastSortableUniqueId);
```

**重要なポイント**:
- `LastSortableUniqueId` はコマンド実行結果で利用可能
- 更新された状態を確実に表示するために、このIDを後続のクエリに渡す
- これによりアプリケーションUIの即時整合性が提供される

### リストクエリのページング

大規模なデータセットの場合、リストクエリでページングを実装できます：

```csharp
[GenerateSerializer]
public record PaginatedListQuery(int Page = 1, int PageSize = 10, string? SearchTerm = null)
    : IMultiProjectionListQuery<AggregateListProjector<YourProjector>, PaginatedListQuery, PaginatedListQuery.ResultRecord>
{
    public static ResultBox<IEnumerable<ResultRecord>> HandleFilter(
        MultiProjectionState<AggregateListProjector<YourProjector>> projection, 
        PaginatedListQuery query, 
        IQueryContext context)
    {
        var filtered = projection.Payload.Aggregates
            .Where(m => m.Value.GetPayload() is YourAggregate);
            
        if (!string.IsNullOrEmpty(query.SearchTerm))
        {
            filtered = filtered.Where(m => 
                ((YourAggregate)m.Value.GetPayload()).Name.Contains(query.SearchTerm));
        }
        
        // ページングのためのSkipとTake（フィルタリングの後に実行）
        int skip = (query.Page - 1) * query.PageSize;
        
        return filtered
            .Select(m => new ResultRecord(
                m.Key, 
                ((YourAggregate)m.Value.GetPayload()).Name))
            .Skip(skip)
            .Take(query.PageSize)
            .ToResultBox();
    }
    
    // その他の必須メソッド...
    
    [GenerateSerializer]
    public record ResultRecord(Guid Id, string Name);
}
```

APIエンドポイントでページングを実装する場合：

```csharp
apiRoute.MapGet("/items",
    async ([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? searchTerm = null,
            [FromServices] SekibanOrleansExecutor executor) =>
    {
        var query = new PaginatedListQuery(page, pageSize, searchTerm);
        var result = await executor.QueryAsync(query).UnwrapBox();
        
        // UIページングのために合計数を含めたい場合
        var countQuery = new CountItemsQuery(searchTerm);
        var totalCount = await executor.QueryAsync(countQuery).UnwrapBox();
        
        return new {
            Items = result,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    });
```