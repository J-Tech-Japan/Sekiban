# ユニットテスト - Sekiban イベントソーシング

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
> - [ユニットテスト](11_unit_testing.md) (現在のページ)
> - [一般的な問題と解決策](12_common_issues.md)
> - [ResultBox](13_result_box.md)

## ユニットテスト

Sekibanはイベントソースアプリケーションをテストするための複数のアプローチを提供しています。簡単さを求めるならインメモリテスト、より複雑なシナリオではOrleans基盤のテストを選ぶことができます。

## テストプロジェクトのセットアップ

まず、テストプロジェクトを作成し、必要なNuGetパッケージを追加します：

```bash
dotnet new xunit -n YourProject.Tests
dotnet add package Sekiban.Testing
```

## 1. SekibanInMemoryTestBaseを使用したインメモリテスト

最も簡単なアプローチは `Sekiban.Pure.xUnit` 名前空間の `SekibanInMemoryTestBase` クラスを使用します：

```csharp
using Sekiban.Pure;
using Sekiban.Pure.xUnit;
using System;
using Xunit;
using YourProject.Domain;
using YourProject.Domain.Aggregates.YourEntity.Commands;
using YourProject.Domain.Aggregates.YourEntity.Payloads;
using YourProject.Domain.Generated;

public class YourTests : SekibanInMemoryTestBase
{
    // ドメインタイプを提供するためにオーバーライド
    protected override SekibanDomainTypes GetDomainTypes() => 
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options);

    [Fact]
    public void SimpleTest()
    {
        // Given - コマンドを実行して応答を取得
        var response1 = GivenCommand(new CreateYourEntity("Name", "Value"));
        Assert.Equal(1, response1.Version);

        // When - 同じ集約に対して別のコマンドを実行
        var response2 = WhenCommand(new UpdateYourEntity(response1.PartitionKeys.AggregateId, "NewValue"));
        Assert.Equal(2, response2.Version);

        // Then - 集約を取得してその状態を検証
        var aggregate = ThenGetAggregate<YourEntityProjector>(response2.PartitionKeys);
        var entity = (YourEntity)aggregate.Payload;
        Assert.Equal("NewValue", entity.Value);
        
        // Then - クエリを実行して結果を検証
        var queryResult = ThenQuery(new YourEntityExistsQuery("Name"));
        Assert.True(queryResult);
    }
}
```

## 2. ResultBoxを使用したメソッドチェーン

より流暢なテストのために、メソッドチェーンをサポートするResultBoxベースのメソッドを使用できます：

```csharp
public class YourTests : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() => 
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options);

    [Fact]
    public void ChainedTest()
        => GivenCommandWithResult(new CreateYourEntity("Name", "Value"))
            .Do(response => Assert.Equal(1, response.Version))
            .Conveyor(response => WhenCommandWithResult(new UpdateYourEntity(response.PartitionKeys.AggregateId, "NewValue")))
            .Do(response => Assert.Equal(2, response.Version))
            .Conveyor(response => ThenGetAggregateWithResult<YourEntityProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<YourEntity>())
            .Do(payload => Assert.Equal("NewValue", payload.Value))
            .Conveyor(_ => ThenQueryWithResult(new YourEntityExistsQuery("Name")))
            .Do(Assert.True)
            .UnwrapBox();
}
```

重要なポイント：
- `Conveyor` は操作をチェーンするために使用され、ある操作の結果を次の入力に変換します
- `Do` はアサーションや副作用を実行するために使用され、結果を変更しません
- 最後の `UnwrapBox` は最終的なResultBoxをアンラップし、いずれかのステップが失敗した場合は例外をスローします

## 3. SekibanOrleansTestBaseを使用したOrleansテスト

Orleans統合でテストするには、`Sekiban.Pure.Orleans.xUnit` 名前空間の `SekibanOrleansTestBase` クラスを使用します：

```csharp
public class YourOrleansTests : SekibanOrleansTestBase<YourOrleansTests>
{
    public override SekibanDomainTypes GetDomainTypes() => 
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options);

    [Fact]
    public void OrleansTest() =>
        GivenCommandWithResult(new CreateYourEntity("Name", "Value"))
            .Do(response => Assert.Equal(1, response.Version))
            .Conveyor(response => WhenCommandWithResult(new UpdateYourEntity(response.PartitionKeys.AggregateId, "NewValue")))
            .Do(response => Assert.Equal(2, response.Version))
            .Conveyor(response => ThenGetAggregateWithResult<YourEntityProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<YourEntity>())
            .Do(payload => Assert.Equal("NewValue", payload.Value))
            .Conveyor(_ => ThenGetMultiProjectorWithResult<AggregateListProjector<YourEntityProjector>>())
            .Do(projector => 
            {
                Assert.Equal(1, projector.Aggregates.Values.Count());
                var entity = (YourEntity)projector.Aggregates.Values.First().Payload;
                Assert.Equal("NewValue", entity.Value);
            })
            .UnwrapBox();
            
    [Fact]
    public void TestSerializable()
    {
        // コマンドがシリアライズ可能かテスト（Orleansにとって重要）
        CheckSerializability(new CreateYourEntity("Name", "Value"));
    }
}
```

## 4. InMemorySekibanExecutorを使用した手動テスト

より複雑なシナリオやカスタムテストセットアップには、`InMemorySekibanExecutor` を手動で作成できます：

```csharp
[Fact]
public async Task ManualExecutorTest()
{
    // インメモリエグゼキューターを作成
    var executor = new InMemorySekibanExecutor(
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options),
        new FunctionCommandMetadataProvider(() => "test"),
        new Repository(),
        new ServiceCollection().BuildServiceProvider());

    // コマンドを実行
    var result = await executor.CommandAsync(new CreateYourEntity("Name", "Value"));
    Assert.True(result.IsSuccess);
    var value = result.GetValue();
    Assert.NotNull(value);
    Assert.Equal(1, value.Version);
    var aggregateId = value.PartitionKeys.AggregateId;

    // 集約をロード
    var aggregateResult = await executor.LoadAggregateAsync<YourEntityProjector>(
        PartitionKeys.Existing<YourEntityProjector>(aggregateId));
    Assert.True(aggregateResult.IsSuccess);
    var aggregate = aggregateResult.GetValue();
    var entity = (YourEntity)aggregate.Payload;
    Assert.Equal("Name", entity.Name);
    Assert.Equal("Value", entity.Value);
}
```

## ワークフローのテスト

```csharp
public class DuplicateCheckWorkflowsTests : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() => 
        YourDomainDomainTypes.Generate(YourDomainEventsJsonContext.Default.Options);

    [Fact]
    public async Task CheckUserIdDuplicate_WhenUserIdExists_ReturnsDuplicate()
    {
        // Arrange - テストしたいIDを持つユーザーを作成
        var existingUserId = "U12345";
        var command = new RegisterUserCommand(
            "John Doe",
            existingUserId,
            "john@example.com");

        // 同じIDでユーザーを登録して確実に存在するようにする
        GivenCommand(command);

        // Act - 同じIDで別のユーザーを登録しようとする
        var result = await DuplicateCheckWorkflows.CheckUserIdDuplicate(command, Executor);

        // Assert
        Assert.True(result.IsDuplicate);
        Assert.Contains(existingUserId, result.ErrorMessage);
        Assert.Null(result.CommandResult);
    }

    [Fact]
    public async Task CheckUserIdDuplicate_WhenUserIdDoesNotExist_ReturnsSuccess()
    {
        // Arrange
        var newUserId = "U67890";
        var command = new RegisterUserCommand(
            "Jane Doe",
            newUserId,
            "jane@example.com");

        // Act
        var result = await DuplicateCheckWorkflows.CheckUserIdDuplicate(command, Executor);

        // Assert
        Assert.False(result.IsDuplicate);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.CommandResult);
    }
}
```

## Given-When-Thenパターンを使用したテスト

Sekibanのテストツールはより表現力豊かなテストのためにGiven-When-Thenパターンをサポートしています：

```csharp
[Fact]
public void UserRegistrationAndConfirmation()
{
    // Given - 登録済みのユーザー
    var registeredUserResponse = GivenCommand(new RegisterUserCommand(
        "John Doe", 
        "john@example.com", 
        "Password123"));
    
    var userId = registeredUserResponse.PartitionKeys.AggregateId;
    
    // Then - ユーザーは未確認状態であるべき
    var unconfirmedAggregate = ThenGetAggregate<UserProjector>(registeredUserResponse.PartitionKeys);
    Assert.IsType<UnconfirmedUser>(unconfirmedAggregate.Payload);
    var unconfirmedUser = (UnconfirmedUser)unconfirmedAggregate.Payload;
    Assert.Equal("John Doe", unconfirmedUser.Name);
    Assert.Equal("john@example.com", unconfirmedUser.Email);
    
    // When - ユーザーを確認
    var confirmationResponse = WhenCommand(new ConfirmUserCommand(userId));
    
    // Then - ユーザーは確認済み状態であるべき
    var confirmedAggregate = ThenGetAggregate<UserProjector>(confirmationResponse.PartitionKeys);
    Assert.IsType<ConfirmedUser>(confirmedAggregate.Payload);
    var confirmedUser = (ConfirmedUser)confirmedAggregate.Payload;
    Assert.Equal("John Doe", confirmedUser.Name);
    Assert.Equal("john@example.com", confirmedUser.Email);
}
```

## マルチプロジェクターのテスト

```csharp
[Fact]
public void MultiProjectorTest()
{
    // Given - 注文が行われた
    var placeOrderResponse = GivenCommand(new PlaceOrderCommand(
        "customer123",
        new[] { new OrderItemDto("product1", 2, 10.0m) }));
        
    // When - 別の注文が行われた
    var placeOrder2Response = WhenCommand(new PlaceOrderCommand(
        "customer123",
        new[] { new OrderItemDto("product2", 1, 15.0m) }));
        
    // Then - OrderStatisticsは両方の注文を反映しているべき
    var statistics = ThenGetMultiProjector<OrderStatisticsProjector>();
    
    Assert.Equal(2, statistics.TotalOrders);
    Assert.Equal(35.0m, statistics.TotalRevenue);
    
    // 顧客統計をチェック
    Assert.True(statistics.CustomerStatistics.TryGetValue("customer123", out var customerStats));
    Assert.Equal(2, customerStats.OrderCount);
    Assert.Equal(35.0m, customerStats.TotalSpent);
    
    // 商品販売をチェック
    Assert.Equal(2, statistics.ProductSales["product1"]);
    Assert.Equal(1, statistics.ProductSales["product2"]);
}
```

## ベストプラクティス

1. **コマンドをテストする**：コマンドが期待されるイベントと状態変更を生成することを検証する
2. **プロジェクターをテストする**：プロジェクターが集約状態を構築するためにイベントを正しく適用することを検証する
3. **クエリをテストする**：クエリが現在の状態に基づいて期待される結果を返すことを検証する
4. **状態遷移をテストする**：特に異なるペイロードタイプを使用する場合、状態遷移が正しく機能することを検証する
5. **エラーケースをテストする**：バリデーションが失敗したときにコマンドが適切に失敗することを検証する
6. **シリアライゼーションをテストする**：Orleansテストでは、コマンドとイベントがシリアライズ可能であることを検証する
7. **GivenCommandをセットアップに使用する**：テスト状態をセットアップするために `GivenCommand` を使用する
8. **WhenCommandをアクションに使用する**：テスト対象のアクションには `WhenCommand` を使用する
9. **ThenGetAggregateとThenQueryをアサーションに使用する**：検証にはこれらのメソッドを使用する
10. **テストを集中させる**：各テストは単一の動作に焦点を当てるべき