# ワークフロー - Sekiban イベントソーシング

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [集約、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
> - [複数集約プロジェクター](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [ワークフロー](06_workflow.md) (現在のページ)
> - [JSONとOrleansのシリアライゼーション](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md)
> - [Orleansセットアップ](10_orleans_setup.md)
> - [ユニットテスト](11_unit_testing.md)
> - [一般的な問題と解決策](12_common_issues.md)
> - [ResultBox](13_result_box.md)

## ワークフローとドメインサービス

Sekibanは、複数の集約にまたがるか、特殊な処理が必要なビジネスロジックをカプセル化するドメインワークフローやサービスの実装をサポートしています。

## ドメインワークフロー

ドメインワークフローは、複数の集約を含むか複雑な検証ロジックを必要とするビジネスプロセスを実装するステートレスなサービスです。特に以下の場合に有用です：

1. **集約を跨ぐ操作**：ビジネスプロセスが複数の集約にまたがる場合
2. **外部データ取得**：ビジネスロジックが外部システムや複数の集約からのデータを必要とする場合
3. **複雑な検証**：検証が複数の集約や外部システムとのチェックを必要とする場合
4. **再利用可能なビジネスロジック**：同じロジックが複数の場所で使用される場合
5. **認証・セキュリティ**：RBACと認証ロジックの実装

```csharp
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Query;
using Sekiban.Pure.ResultBoxes;
using System;
using System.Threading.Tasks;

// 重複チェックのためのドメインワークフローの例
namespace YourProject.Domain.Workflows;

public static class DuplicateCheckWorkflows
{
    // 重複チェック操作の結果タイプ
    public class DuplicateCheckResult
    {
        public bool IsDuplicate { get; }
        public string? ErrorMessage { get; }
        public object? CommandResult { get; }

        private DuplicateCheckResult(bool isDuplicate, string? errorMessage, object? commandResult)
        {
            IsDuplicate = isDuplicate;
            ErrorMessage = errorMessage;
            CommandResult = commandResult;
        }

        public static DuplicateCheckResult Duplicate(string errorMessage) => 
            new(true, errorMessage, null);

        public static DuplicateCheckResult Success(object commandResult) => 
            new(false, null, commandResult);
    }

    // 登録する前にIDの重複をチェックするワークフローメソッド
    public static async Task<DuplicateCheckResult> CheckUserIdDuplicate(
        RegisterUserCommand command,
        ISekibanExecutor executor)
    {
        // ユーザーIDがすでに存在するかチェック
        var userIdExists = await executor.QueryAsync(new UserIdExistsQuery(command.UserId)).UnwrapBox();
        if (userIdExists)
        {
            return DuplicateCheckResult.Duplicate($"ID '{command.UserId}' を持つユーザーはすでに存在します");
        }
        
        // 重複がなければ、コマンドを実行
        var result = await executor.CommandAsync(command).UnwrapBox();
        return DuplicateCheckResult.Success(result);
    }
}
```

**重要なポイント**:
- ワークフローは通常、静的クラスと静的メソッドとして実装されます
- `Workflows` フォルダまたは名前空間に配置すべきです
- テスト容易性のために `ISekibanExecutor` インターフェースを使用すべきです
- 成功/失敗情報をカプセル化するドメイン固有の結果型を返すべきです
- APIエンドポイントや他のサービスから呼び出すことができます

## APIエンドポイントでのワークフローの使用

```csharp
// Program.csで
apiRoute.MapPost("/users/register",
    async ([FromBody] RegisterUserCommand command, [FromServices] SekibanOrleansExecutor executor) => 
    {
        // 重複をチェックするためにワークフローを使用
        var result = await DuplicateCheckWorkflows.CheckUserIdDuplicate(command, executor);
        if (result.IsDuplicate)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "ユーザーID重複",
                detail: result.ErrorMessage);
        }
        return Results.Ok(result.CommandResult);
    });
```

## 例：注文処理ワークフロー

複数の集約と検証を含む、より複雑な注文処理ワークフローを作成してみましょう：

```csharp
public static class OrderProcessingWorkflow
{
    public record OrderProcessingResult
    {
        public bool IsSuccess { get; }
        public string? ErrorMessage { get; }
        public Guid? OrderId { get; }
        
        private OrderProcessingResult(bool isSuccess, string? errorMessage, Guid? orderId)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
            OrderId = orderId;
        }
        
        public static OrderProcessingResult Success(Guid orderId) => new(true, null, orderId);
        public static OrderProcessingResult Failure(string errorMessage) => new(false, errorMessage, null);
    }
    
    public static async Task<OrderProcessingResult> ProcessOrder(
        CreateOrderCommand command,
        ISekibanExecutor executor)
    {
        // 1. 顧客が存在するかチェック
        var customerExists = await executor.QueryAsync(
            new CustomerExistsQuery(command.CustomerId)).UnwrapBox();
            
        if (!customerExists)
        {
            return OrderProcessingResult.Failure($"顧客 '{command.CustomerId}' が見つかりません");
        }
        
        // 2. 各アイテムの商品在庫をチェック
        foreach (var item in command.Items)
        {
            var inventory = await executor.QueryAsync(
                new GetProductInventoryQuery(item.ProductId)).UnwrapBox();
                
            if (inventory < item.Quantity)
            {
                return OrderProcessingResult.Failure(
                    $"商品 '{item.ProductId}' の在庫が不足しています。 " +
                    $"要求数: {item.Quantity}, 在庫数: {inventory}");
            }
        }
        
        // 3. 注文を作成
        var orderResult = await executor.CommandAsync(command).UnwrapBox();
        var orderId = orderResult.PartitionKeys.AggregateId;
        
        // 4. 各商品の在庫を更新
        foreach (var item in command.Items)
        {
            await executor.CommandAsync(new DecrementInventoryCommand(
                item.ProductId, 
                item.Quantity, 
                orderId));
        }
        
        // 5. 注文IDを含む成功結果を返す
        return OrderProcessingResult.Success(orderId);
    }
}
```

## ワークフローでのSagaパターンの実装

補償/ロールバックが必要な可能性のあるより複雑なビジネスプロセスの場合、Sagaパターンを実装できます：

```csharp
public static class PaymentProcessingSaga
{
    public record PaymentProcessingResult
    {
        public bool IsSuccess { get; }
        public string? ErrorMessage { get; }
        public Guid? TransactionId { get; }
        
        private PaymentProcessingResult(bool isSuccess, string? errorMessage, Guid? transactionId)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
            TransactionId = transactionId;
        }
        
        public static PaymentProcessingResult Success(Guid transactionId) => 
            new(true, null, transactionId);
            
        public static PaymentProcessingResult Failure(string errorMessage) => 
            new(false, errorMessage, null);
    }
    
    public static async Task<PaymentProcessingResult> ProcessPayment(
        ProcessPaymentCommand command,
        ISekibanExecutor executor)
    {
        // 1. 顧客口座から資金を予約
        var reserveResult = await executor.CommandAsync(
            new ReserveFundsCommand(command.AccountId, command.Amount, command.OrderId)).UnwrapBox();
            
        if (reserveResult is CommandExecutionError error)
        {
            return PaymentProcessingResult.Failure($"資金の予約に失敗しました: {error.Message}");
        }
        
        try
        {
            // 2. 決済プロバイダーに課金
            var chargeResult = await executor.CommandAsync(
                new ChargePaymentProviderCommand(command.PaymentMethod, command.Amount)).UnwrapBox();
                
            if (chargeResult is CommandExecutionError chargeError)
            {
                // 補償: 予約した資金を解放
                await executor.CommandAsync(
                    new ReleaseFundsCommand(command.AccountId, command.Amount, command.OrderId));
                    
                return PaymentProcessingResult.Failure($"決済プロバイダーエラー: {chargeError.Message}");
            }
            
            // 3. 決済を確認
            var confirmResult = await executor.CommandAsync(
                new ConfirmPaymentCommand(command.OrderId, command.Amount)).UnwrapBox();
                
            return PaymentProcessingResult.Success(confirmResult.PartitionKeys.AggregateId);
        }
        catch (Exception ex)
        {
            // 補償: 予約した資金を解放
            await executor.CommandAsync(
                new ReleaseFundsCommand(command.AccountId, command.Amount, command.OrderId));
                
            return PaymentProcessingResult.Failure($"予期しないエラー: {ex.Message}");
        }
    }
}
```

## ワークフローのテスト

ワークフローは、他のSekibanコンポーネントと同じインメモリテストアプローチを使用してテストできます：

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

        // 同じIDでユーザーを登録して存在を確認
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

**重要なポイント**:
- ワークフローのテストには `SekibanInMemoryTestBase` を使用
- 基底クラスは `ISekibanExecutor` を実装する `Executor` プロパティを提供
- テスト状態をセットアップするには `GivenCommand` を使用
- 成功と失敗の両方のシナリオをテスト

## ワークフローのベストプラクティス

1. **ワークフローをステートレスに保つ**: ワークフローはステートレスであるべきで、状態管理は集約に委譲すべき
2. **依存性注入を使用する**: サービスをワークフローに注入するために依存性注入を使用する
3. **ドメイン固有の結果型**: 成功/失敗情報をカプセル化するドメイン固有の結果型を返す
4. **エラー処理**: ワークフローまたはAPIエンドポイントの適切なレベルでエラーを処理する
5. **徹底的にテストする**: エッジケースやエラーシナリオを含めて、ワークフローを徹底的にテストする
6. **べき等性を考慮する**: 可能な場合、リトライを処理するためにワークフローをべき等にする
7. **補償アクションを使用する**: 複雑なワークフローの場合、部分的な変更をロールバックするための補償アクションを実装する