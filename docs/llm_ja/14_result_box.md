# ResultBox - Sekiban イベントソーシング

> **ナビゲーション**
> - [コア概念](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [アグリゲート、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
> - [複数アグリゲートプロジェクター](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [ワークフロー](06_workflow.md)
> - [JSONとOrleansシリアライゼーション](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md)
> - [Orleans設定](10_orleans_setup.md)
> - [Dapr設定](11_dapr_setup.md)
> - [ユニットテスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md) (現在位置)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイメントガイド](16_deployment.md)

## ResultBoxの紹介

ResultBoxは`ResultBoxes`パッケージから提供される強力なユーティリティ型で、SekibanドメインとAPIコードにとって不可欠です。エラー処理、メソッドチェーン、操作が適切に完了または失敗した場合の処理を支援します。

## 主要な概念

### ResultBoxとは

ResultBoxは、操作が成功したかどうかの情報と共に結果値をラップするコンテナです。以下の機能を提供します：

1. **エラー処理** - 例外をスローせずに安全にエラーを管理
2. **メソッドチェーン** - 操作の流暢な組み合わせを可能にする
3. **アンラップ** - 最終的な値を取り出し、いずれかのステップが失敗した場合は例外をスロー

### 基本的な使用法

ResultBoxは失敗する可能性のある操作を処理するためにSekiban全体で使用され、流暢なエラー処理を可能にします。以下は簡単な例です：

```csharp
// ResultBoxを返すメソッド
public ResultBox<User> GetUserById(string id)
{
    if (string.IsNullOrEmpty(id))
    {
        return ResultBox.Error<User>("ユーザーIDは空にできません");
    }
    
    var user = repository.FindUser(id);
    if (user == null)
    {
        return ResultBox.Error<User>($"ID {id} のユーザーが見つかりません");
    }
    
    return ResultBox.Ok(user);
}
```

## ResultBoxを使用したメソッドチェーン

ResultBoxの最も強力な機能の1つは、拡張メソッドを使用して操作を連鎖させる能力です：

### 主要な拡張メソッド

1. **Conveyor** - 操作が成功した場合、結果を新しいResultBoxに変換します
2. **Do** - 操作が成功した場合、値に対してアクションを実行します
3. **UnwrapBox** - ResultBoxから値を取り出し、操作が失敗した場合は例外をスローします

### API実装でのメソッドチェーン

ResultBoxはAPIエンドポイントでコマンド実行を処理するためによく使用されます：

```csharp
// ResultBoxを使用したAPIエンドポイントの例
[HttpPost("createuser")]
public async Task<ActionResult<CommandResponseSimple>> CreateUser(
    [FromBody] CreateUserCommand command,
    [FromServices] SekibanOrleansExecutor executor)
{
    return await executor.CommandAsync(command)
        .ToSimpleCommandResponse()  // より単純なレスポンス形式に変換
        .UnwrapBox();  // 結果をアンラップするか例外をスロー
}
```

このパターンでは：
1. `CommandAsync`はコマンドレスポンスを含むResultBoxを返します
2. `ToSimpleCommandResponse()`はより簡潔なクライアント向け形式に変換します
3. `UnwrapBox()`は最終的な値を取り出すか、適切な例外をスローします

### テストでのメソッドチェーン

ResultBoxはユニットテストで、流暢なテストチェーンを作成するのに特に便利です：

```csharp
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
```

このテストでは：
1. 各操作は`Conveyor`を使用して次の操作に連鎖します
2. アサーションは`Do`を使用してチェーンを中断せずに行われます
3. 最後の`UnwrapBox`はチェーン内の任意の失敗が例外をスローすることを保証します

## ResultBoxを使用したエラー処理

ResultBoxは、対処する準備ができるまで例外をスローせずに、エレガントにエラーを処理できます：

```csharp
public async Task<ResultBox<CommandResponseSimple>> ExecuteCommand(CreateItemCommand command)
{
    try
    {
        // コマンドを実行
        var result = await executor.CommandAsync(command);
        
        // 続行する前にコマンドが成功したかチェック
        if (!result.IsSuccess)
        {
            return ResultBox.Error<CommandResponseSimple>(result.ErrorMessage);
        }
        
        // より単純なレスポンスに変換
        return result.ToSimpleCommandResponse();
    }
    catch (Exception ex)
    {
        // 例外をResultBoxにラップ
        return ResultBox.Error<CommandResponseSimple>(ex.Message);
    }
}
```

## ベストプラクティス

1. **メソッドチェーンを優先する** - エラーのコンテキストを維持するために、早期にアンラップするのではなくメソッドチェーンを使用する
2. **境界でアンラップする** - APIコントローラーなどのアプリケーションの境界でのみ`UnwrapBox()`を呼び出す
3. **意味のあるエラーメッセージ** - エラーResultBoxを作成する際に明確なエラーメッセージを提供する
4. **Conveyorで変換する** - 成功/失敗の状態を維持しながら値を変換するために`Conveyor`を使用する
5. **Doでサイドエフェクト** - チェーンを中断せずにアサーションやロギングには`Do`を使用する

## 結論

ResultBoxはSekibanの基本的な部分であり、流暢なエラー処理、エレガントなメソッドチェーン、そしてクリーンなAPI設計を可能にします。ResultBoxを理解して適切に使用することで、Sekibanアプリケーションでより堅牢で読みやすく保守しやすいコードを書くことができます。