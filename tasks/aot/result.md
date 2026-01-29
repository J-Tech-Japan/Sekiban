# AOT対応 プロジェクト再構成 - 実装結果

## 実装完了日
2026年1月29日

## 実装概要

設計書（claude.md）に基づき、AOT対応のためのプロジェクト分離を完了しました。

## 完了したフェーズ

### Phase 1: Core から ICommand, ICoreCommandContext を Core.Model に移動 ✅

- `ICommand` を `Sekiban.Dcb.Core` から `Sekiban.Dcb.Core.Model/Commands/` に移動
- `ICoreCommandContext` を `Sekiban.Dcb.Core.Model/Commands/` に追加
- Core.Model は `<IsAotCompatible>true</IsAotCompatible>` を維持

### Phase 2: Sekiban.Dcb.WithResult.Model プロジェクト作成 ✅

新規プロジェクト: `dcb/src/Sekiban.Dcb.WithResult.Model/`

**作成ファイル:**
- `Sekiban.Dcb.WithResult.Model.csproj` - AOT対応、Core.Model参照
- `Commands/ICommandHandler.cs` - ResultBox<EventOrNone>を返すハンドラー
- `Commands/ICommandContext.cs` - ICoreCommandContextのエイリアス
- `Commands/ICommandWithHandler.cs` - コマンドとハンドラーの統合インターフェース
- `MultiProjections/IMultiProjector.cs` - ICoreMultiProjectorのエイリアス
- `MultiProjections/IMultiProjectorWithCustomSerialization.cs` - カスタムシリアライゼーション対応
- `Queries/IMultiProjectionQuery.cs` - 単一結果クエリ
- `Queries/IMultiProjectionListQuery.cs` - リストクエリ

### Phase 3: Sekiban.Dcb.WithoutResult.Model プロジェクト作成 ✅

新規プロジェクト: `dcb/src/Sekiban.Dcb.WithoutResult.Model/`

**作成ファイル:**
- `Sekiban.Dcb.WithoutResult.Model.csproj` - AOT対応、ResultBox無しのAPI
- `Commands/ICommandHandler.cs` - EventOrNoneを返すハンドラー（例外ベース）
- `Commands/ICommandContext.cs` - ResultBox無しのコンテキスト
- `Commands/ICommandWithHandler.cs` - コマンドとハンドラーの統合インターフェース
- `MultiProjections/IMultiProjector.cs` - Tを返すプロジェクター（例外ベース）
- `MultiProjections/IMultiProjectorWithCustomSerialization.cs` - カスタムシリアライゼーション対応
- `Queries/IMultiProjectionQuery.cs` - 単一結果クエリ（例外ベース）
- `Queries/IMultiProjectionListQuery.cs` - リストクエリ（例外ベース）

### Phase 4: 既存プロジェクトの参照更新 ✅

**更新されたプロジェクト:**

| プロジェクト | 追加された参照 |
|------------|---------------|
| Sekiban.Dcb.WithResult | WithResult.Model |
| Sekiban.Dcb.WithoutResult | WithoutResult.Model |
| Sekiban.Dcb.Orleans.WithResult | WithResult.Model |
| Sekiban.Dcb.Orleans.WithoutResult | WithoutResult.Model |

### Phase 5: ビルド検証・テスト ✅

- 全プロジェクトのビルド成功
- 既存テスト全て合格（3つのTagValidator事前失敗を除く）

## ワークフロー更新 ✅

`.github/workflows/packagesDcb.yml` を更新し、新しいModelパッケージを追加:

```yaml
dotnet pack dcb/src/Sekiban.Dcb.Core.Model/Sekiban.Dcb.Core.Model.csproj ...
dotnet pack dcb/src/Sekiban.Dcb.Core/Sekiban.Dcb.Core.csproj ...
dotnet pack dcb/src/Sekiban.Dcb.WithResult.Model/Sekiban.Dcb.WithResult.Model.csproj ...
dotnet pack dcb/src/Sekiban.Dcb.WithoutResult.Model/Sekiban.Dcb.WithoutResult.Model.csproj ...
dotnet pack dcb/src/Sekiban.Dcb.WithResult/Sekiban.Dcb.WithResult.csproj ...
dotnet pack dcb/src/Sekiban.Dcb.WithoutResult/Sekiban.Dcb.WithoutResult.csproj ...
# ... 残りのパッケージ
```

## AOT設定確認 ✅

3つのModelプロジェクト全てで正しく設定:

| プロジェクト | IsAotCompatible | 行番号 |
|------------|-----------------|-------|
| Sekiban.Dcb.Core.Model | true | 26 |
| Sekiban.Dcb.WithResult.Model | true | 25 |
| Sekiban.Dcb.WithoutResult.Model | true | 25 |

## 新しいプロジェクト構成

```
dcb/src/
├── Sekiban.Dcb.Core.Model/           # 共通型（ICore系, ResultBox依存あり）
│   ├── Commands/
│   │   ├── ICommand.cs               # Core から移動
│   │   └── ICoreCommandContext.cs    # Core から移動
│   └── ...
│
├── Sekiban.Dcb.WithResult.Model/     # NEW: ResultBox API のモデル定義
│   ├── Commands/
│   │   ├── ICommandHandler.cs        # ResultBox<EventOrNone> を返す
│   │   ├── ICommandContext.cs        # = ICoreCommandContext エイリアス
│   │   └── ICommandWithHandler.cs
│   ├── MultiProjections/
│   │   ├── IMultiProjector.cs
│   │   └── IMultiProjectorWithCustomSerialization.cs
│   └── Queries/
│       ├── IMultiProjectionQuery.cs
│       └── IMultiProjectionListQuery.cs
│
├── Sekiban.Dcb.WithoutResult.Model/  # NEW: Exception API のモデル定義
│   ├── Commands/
│   │   ├── ICommandHandler.cs        # EventOrNone を返す（例外ベース）
│   │   ├── ICommandContext.cs        # 独自定義（ResultBox無し）
│   │   └── ICommandWithHandler.cs
│   ├── MultiProjections/
│   │   ├── IMultiProjector.cs        # T を返す（例外ベース）
│   │   └── IMultiProjectorWithCustomSerialization.cs
│   └── Queries/
│       ├── IMultiProjectionQuery.cs
│       └── IMultiProjectionListQuery.cs
│
├── Sekiban.Dcb.WithResult/           # ResultBox API 実装（Model参照追加）
├── Sekiban.Dcb.WithoutResult/        # Exception API 実装（Model参照追加）
├── Sekiban.Dcb.Orleans.WithResult/   # Orleans ResultBox版（Model参照追加）
└── Sekiban.Dcb.Orleans.WithoutResult/ # Orleans Exception版（Model参照追加）
```

## 達成した目標

1. **AOT対応**: 3つのModelプロジェクト全てで `IsAotCompatible=true`
2. **ResultBox無しのドメイン定義**: `WithoutResult.Model` のみを参照することで、ResultBoxを使わないドメイン定義が可能
3. **後方互換性維持**: 既存の名前空間とインターフェースを維持
4. **ICore系の共用**: Core.Model のICore系インターフェースを基盤として両方のAPIで共有

## 使用例

### WithoutResult版（AOT推奨、ResultBox無し）

```csharp
using Sekiban.Dcb.Commands;      // ICommand
using Sekiban.Dcb.Events;        // IEventPayload
using Sekiban.Dcb.MultiProjections; // IMultiProjector (例外ベース)

public record CreateUserCommand(string UserId, string Name) : ICommand;

public record CreateUserHandler : ICommandHandler<CreateUserCommand>
{
    public static async Task<EventOrNone> HandleAsync(
        CreateUserCommand command,
        ICommandContext context)
    {
        // 例外をスロー（ResultBox不使用）
        var exists = await context.TagExistsAsync(new UserTag(command.UserId));
        if (exists)
            throw new InvalidOperationException($"User already exists");

        return EventOrNone.Event(new UserCreated(command.UserId, command.Name));
    }
}
```

### WithResult版（ResultBox使用）

```csharp
using Sekiban.Dcb.Commands;      // ICommand
using Sekiban.Dcb.Events;        // IEventPayload
using Sekiban.Dcb.MultiProjections; // IMultiProjector (ResultBox版)
using ResultBoxes;

public record CreateUserHandler : ICommandHandler<CreateUserCommand>
{
    public static async Task<ResultBox<EventOrNone>> HandleAsync(
        CreateUserCommand command,
        ICommandContext context)
    {
        return await context.TagExistsAsync(new UserTag(command.UserId))
            .Conveyor(exists => exists
                ? ResultBox<EventOrNone>.FromException(
                    new InvalidOperationException($"User already exists"))
                : EventOrNone.Event(new UserCreated(command.UserId, command.Name)));
    }
}
```

## NuGetパッケージ

リリース時に以下のパッケージが公開されます:

- `Sekiban.Dcb.Core.Model` - 共通型定義
- `Sekiban.Dcb.WithResult.Model` - ResultBox API インターフェース
- `Sekiban.Dcb.WithoutResult.Model` - Exception API インターフェース

## 関連Issue

GitHub Issue: #901
Branch: `feature/901-aot-model-separation`
