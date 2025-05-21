# 複数集約プロジェクター - Sekiban イベントソーシング

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [集約、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
> - [複数集約プロジェクター](04_multiple_aggregate_projector.md) (現在のページ)
> - [クエリ](05_query.md)
> - [ワークフロー](06_workflow.md)
> - [JSONとOrleansのシリアライゼーション](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md)
> - [Orleansセットアップ](10_orleans_setup.md)
> - [ユニットテスト](11_unit_testing.md)
> - [一般的な問題と解決策](12_common_issues.md)
> - [ResultBox](13_result_box.md)

## 複数集約プロジェクター

単一集約プロジェクターが単一のエンティティの状態構築に焦点を当てているのに対して、複数集約プロジェクターを使用すると、複数の集約からデータを組み合わせたビューや特殊なプロジェクションを作成できます。

## 複数集約プロジェクターを使用する場合

1. **集約を跨ぐビュー**：複数の集約からデータを組み合わせたビューが必要な場合
2. **特殊なプロジェクション**：集約データの特殊なビュー（例：統計、非正規化されたビュー）が必要な場合
3. **フィルタリングされたコレクション**：特定の基準に基づいて集約のフィルタリングされたサブセットが必要な場合
4. **リアルタイムダッシュボードデータ**：集約全体でカウンターやサマリーを維持する必要がある場合

## 組み込みのマルチプロジェクター

Sekibanはいくつかの組み込みマルチプロジェクタータイプを提供しています：

### 1. AggregateListProjector

これは最も一般的に使用されるマルチプロジェクターで、特定のタイプのすべての集約のコレクションを維持します：

```csharp
// これは組み込みであり、定義する必要はありません
public class AggregateListProjector<TProjector> : IMultiProjector
    where TProjector : IAggregateProjector
{
    public Dictionary<Guid, Aggregate> Aggregates { get; }
}
```

**クエリでの使用法**:
```csharp
[GenerateSerializer]
public record ListYourEntitiesQuery() 
    : IMultiProjectionListQuery<AggregateListProjector<YourEntityProjector>, ListYourEntitiesQuery, YourEntityResult>
{
    public static ResultBox<IEnumerable<YourEntityResult>> HandleFilter(
        MultiProjectionState<AggregateListProjector<YourEntityProjector>> state,
        ListYourEntitiesQuery query,
        IQueryContext context)
    {
        return state.Payload.Aggregates
            .Where(m => m.Value.GetPayload() is YourEntity)
            .Select(m => MapToResult((YourEntity)m.Value.GetPayload(), m.Key))
            .ToResultBox();
    }
    
    private static YourEntityResult MapToResult(YourEntity entity, Guid id) =>
        new(id, entity.Name, entity.Description);
        
    // その他の必須メソッド...
}
```

### 2. EventHistoryProjector

集約のイベント履歴全体を維持します：

```csharp
// これは組み込みであり、定義する必要はありません
public class EventHistoryProjector<TProjector> : IMultiProjector
    where TProjector : IAggregateProjector
{
    public Dictionary<Guid, List<IEvent>> EventHistories { get; }
}
```

**使用例**:
```csharp
[GenerateSerializer]
public record GetEventHistoryQuery(Guid AggregateId)
    : IMultiProjectionQuery<EventHistoryProjector<YourEntityProjector>, GetEventHistoryQuery, List<EventHistoryItem>>
{
    public static ResultBox<List<EventHistoryItem>> HandleQuery(
        MultiProjectionState<EventHistoryProjector<YourEntityProjector>> state,
        GetEventHistoryQuery query,
        IQueryContext context)
    {
        if (!state.Payload.EventHistories.TryGetValue(query.AggregateId, out var events))
        {
            return new List<EventHistoryItem>();
        }
        
        return events
            .Select(e => new EventHistoryItem(
                e.Id, 
                e.Timestamp, 
                e.Version, 
                e.GetPayload().GetType().Name))
            .ToList();
    }
}

[GenerateSerializer]
public record EventHistoryItem(Guid Id, DateTime Timestamp, int Version, string EventType);
```

## カスタムマルチプロジェクターの作成

ドメインの特殊なビューを維持するためにカスタムマルチプロジェクターを作成できます：

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Projectors;
using System;
using System.Collections.Generic;
using System.Linq;

[GenerateSerializer]
public class OrderStatisticsProjector : IMultiProjector
{
    [Id(0)]
    public int TotalOrders { get; private set; }
    
    [Id(1)]
    public decimal TotalRevenue { get; private set; }
    
    [Id(2)]
    public Dictionary<string, int> ProductSales { get; private set; } = new();
    
    [Id(3)]
    public Dictionary<string, CustomerStats> CustomerStatistics { get; private set; } = new();
    
    [GenerateSerializer]
    public record CustomerStats(int OrderCount, decimal TotalSpent);
}
```

**プロジェクターの実装**:

```csharp
public class OrderStatisticsProjectorSubscriber : IMultiProjectorEventSubscriber
{
    public MultiProjectorSubscribers GetSubscribers() => new()
    {
        GetAggregateSubscriber<OrderProjector>(),
        GetAggregateSubscriber<ProductProjector>()
    };
    
    private EventSubscriber<OrderStatisticsProjector> GetAggregateSubscriber<TProjector>()
        where TProjector : IAggregateProjector, new()
    {
        var subscriber = new EventSubscriber<OrderStatisticsProjector>();
        
        subscriber.Subscribe<OrderPlaced, TProjector>((state, ev, aggregate) =>
        {
            var orderPlaced = (OrderPlaced)ev.GetPayload();
            
            state.Payload.TotalOrders++;
            state.Payload.TotalRevenue += orderPlaced.TotalAmount;
            
            // 顧客統計を更新
            if (!state.Payload.CustomerStatistics.TryGetValue(orderPlaced.CustomerId, out var customerStats))
            {
                customerStats = new CustomerStats(0, 0);
            }
            
            state.Payload.CustomerStatistics[orderPlaced.CustomerId] = new CustomerStats(
                customerStats.OrderCount + 1,
                customerStats.TotalSpent + orderPlaced.TotalAmount
            );
            
            // 商品販売を更新
            foreach (var item in orderPlaced.Items)
            {
                if (!state.Payload.ProductSales.TryGetValue(item.ProductId, out var count))
                {
                    count = 0;
                }
                
                state.Payload.ProductSales[item.ProductId] = count + item.Quantity;
            }
        });
        
        // 必要に応じて他のイベントを購読
        
        return subscriber;
    }
}
```

**マルチプロジェクターの登録**:

マルチプロジェクターが `IMultiProjector` を実装していれば、ソース生成によって自動的に登録されます。そしてクエリで使用できます：

```csharp
[GenerateSerializer]
public record GetOrderStatisticsQuery() 
    : IMultiProjectionQuery<OrderStatisticsProjector, GetOrderStatisticsQuery, OrderStatistics>
{
    public static ResultBox<OrderStatistics> HandleQuery(
        MultiProjectionState<OrderStatisticsProjector> state,
        GetOrderStatisticsQuery query,
        IQueryContext context)
    {
        return new OrderStatistics(
            state.Payload.TotalOrders,
            state.Payload.TotalRevenue,
            state.Payload.ProductSales.OrderByDescending(x => x.Value).Take(5).ToDictionary(k => k.Key, v => v.Value),
            state.Payload.CustomerStatistics.Count
        );
    }
}

[GenerateSerializer]
public record OrderStatistics(
    int TotalOrders,
    decimal TotalRevenue,
    Dictionary<string, int> TopSellingProducts,
    int TotalCustomers
);
```

## パフォーマンスに関する考慮事項

マルチプロジェクターは、特に多くのイベントを処理したり、大きなコレクションを維持する必要がある場合、リソースを消費する可能性があります。以下のベストプラクティスを検討してください：

1. **イベントを選択的に処理する**：プロジェクションに必要なイベントのみを購読する
2. **効率的なデータ構造を使用する**：プロジェクションデータに適切なデータ構造を選択する
3. **スナップショットの検討**：大規模なプロジェクションの場合、スナップショットの実装を検討する
4. **クエリの最適化**：特に大きなデータセットに対するクエリハンドラーを効率的に保つ

## マルチプロジェクターのテスト

通常の集約と同じアプローチを使用してマルチプロジェクターをテストできます：

```csharp
[Fact]
public void OrderStatistics_ShouldUpdateCorrectly()
{
    // 準備
    var orderCommand = new PlaceOrder("C001", new[]
    {
        new OrderItem("P001", 2, 10.0m),
        new OrderItem("P002", 1, 15.0m)
    });
    
    // 実行
    GivenCommand(orderCommand);
    
    // 検証
    var statistics = ThenQuery(new GetOrderStatisticsQuery());
    
    Assert.Equal(1, statistics.TotalOrders);
    Assert.Equal(35.0m, statistics.TotalRevenue);
    Assert.Equal(2, statistics.TopSellingProducts.Count);
    Assert.Equal(1, statistics.TotalCustomers);
}
```