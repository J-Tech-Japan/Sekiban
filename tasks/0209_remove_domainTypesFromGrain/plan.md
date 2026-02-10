# 実装方針: Tag Grains から DcbDomainTypes を除去

## 現状

PR #914 (merged) で MultiProjectionGrain の DcbDomainTypes 除去は **全て完了**。Issue #913 の DoD は MultiProjectionGrain について全て達成済み。

Grain フォルダ内で `DcbDomainTypes` が残っているのは **TagStateGrain.cs** と **TagConsistentGrain.cs** の 2 ファイル（各4箇所、計8箇所）のみ。

## 方針

Actor のコンストラクタを `DcbDomainTypes` → Core.Model に定義済みの個別インターフェースに分解する。

**理由:**
- `ITagProjectionRuntime` は Orleans.Core に定義されており、Core の Actor からは参照不可（依存方向: Core → Core.Model のみ）
- `ITagProjectorTypes`, `ITagTypes`, `ITagStatePayloadTypes` は全て Core.Model (`Sekiban.Dcb.Domains` 名前空間) に定義済み → Actor から直接参照可能
- IProjectionActorHost のようなホストパターンは Tag grain の規模では不要（設計ドキュメント: "Keep tag grains native-only"）

## 変更ファイル一覧

### Actor 層 (Sekiban.Dcb.Core)

#### 1. `Actors/GeneralTagConsistentActor.cs`

`DcbDomainTypes` の使用は 1 箇所のみ: `_domainTypes.TagTypes.GetTag(_tagName)` (L215)

- コンストラクタ: `DcbDomainTypes domainTypes` → `ITagTypes tagTypes`
- フィールド: `_domainTypes` (DcbDomainTypes) → `_tagTypes` (ITagTypes)
- L215: `_domainTypes.TagTypes.GetTag(_tagName)` → `_tagTypes.GetTag(_tagName)`
- using 追加: `using Sekiban.Dcb.Domains;`

#### 2. `Actors/GeneralTagStateActor.cs`

`DcbDomainTypes` の使用は 4 箇所:

| 行 | Before | After |
|----|--------|-------|
| L100 | `_domainTypes.TagStatePayloadTypes.SerializePayload(...)` | `_tagStatePayloadTypes.SerializePayload(...)` |
| L172 | `_domainTypes.TagProjectorTypes.GetProjectorFunction(...)` | `_tagProjectorTypes.GetProjectorFunction(...)` |
| L182 | `_domainTypes.TagProjectorTypes.GetProjectorVersion(...)` | `_tagProjectorTypes.GetProjectorVersion(...)` |
| L312 | `_domainTypes.TagTypes.GetTag(...)` | `_tagTypes.GetTag(...)` |

- 3 つのコンストラクタオーバーロード全てで `DcbDomainTypes domainTypes` → `ITagProjectorTypes tagProjectorTypes, ITagTypes tagTypes, ITagStatePayloadTypes tagStatePayloadTypes`
- フィールド: `_domainTypes` → `_tagProjectorTypes`, `_tagTypes`, `_tagStatePayloadTypes`
- using 追加: `using Sekiban.Dcb.Domains;`

#### 3. `InMemory/InMemoryObjectAccessor.cs`

`_domainTypes` フィールド自体は `GeneralMultiProjectionActor` 生成で使うので残す。Actor 生成箇所のみ変更:

- L114-118: `..., _domainTypes)` → `..., _domainTypes.TagTypes)`
- L125: `..., _domainTypes, this)` → `..., _domainTypes.TagProjectorTypes, _domainTypes.TagTypes, _domainTypes.TagStatePayloadTypes, this)`

### Grain 層 (Sekiban.Dcb.Orleans.Core)

#### 4. `Grains/TagConsistentGrain.cs`

- コンストラクタ: `DcbDomainTypes domainTypes` → `ITagTypes tagTypes`（DI 注入）
- フィールド: `_domainTypes` → `_tagTypes`
- L95: `..., _domainTypes)` → `..., _tagTypes)`
- using 追加: `using Sekiban.Dcb.Domains;`

#### 5. `Grains/TagStateGrain.cs`

- コンストラクタ: `DcbDomainTypes domainTypes` を削除し、`ITagProjectorTypes tagProjectorTypes, ITagTypes tagTypes, ITagStatePayloadTypes tagStatePayloadTypes` を追加
- `_domainTypes` フィールドを 3 フィールドに分解
- Actor 生成時に個別インターフェースを渡す
- using 追加: `using Sekiban.Dcb.Domains;`

### DI 登録

#### 6-8. `internalUsages/*/Program.cs` (3 ファイル)

既存の Runtime 登録ブロックの直後に追加:

```csharp
builder.Services.AddSingleton<ITagProjectorTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().TagProjectorTypes);
builder.Services.AddSingleton<ITagTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().TagTypes);
builder.Services.AddSingleton<ITagStatePayloadTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().TagStatePayloadTypes);
```

対象:
- `DcbOrleans.ApiService/Program.cs`
- `DcbOrleans.WithoutResult.ApiService/Program.cs`
- `DcbOrleansDynamoDB.WithoutResult.ApiService/Program.cs`

### テスト

#### 9. `tests/Sekiban.Dcb.Orleans.Tests/MinimalOrleansTests.cs`

DI 登録を Program.cs と同様に追加。

#### 10-16. `tests/Sekiban.Dcb.WithResult.Tests/` 内テストファイル (7 ファイル)

コンストラクタ引数を `_domainTypes` → `_domainTypes.TagTypes` (TagConsistent) / `_domainTypes.TagProjectorTypes, _domainTypes.TagTypes, _domainTypes.TagStatePayloadTypes` (TagState) に変更。

- `GeneralTagStateActorTests.cs`
- `GeneralTagConsistentActorTests.cs`
- `GeneralTagStateActorIncrementalTests.cs`
- `TagConsistentActorCatchupTest.cs`
- `TagConsistentActorReservationWindowTest.cs`
- `TagConsistentActorOptionsTest.cs`
- `InMemoryActorTests.cs`

## 実装順序

1. `GeneralTagConsistentActor.cs` — `DcbDomainTypes` → `ITagTypes`
2. `GeneralTagStateActor.cs` — `DcbDomainTypes` → 3 個別インターフェース
3. `InMemoryObjectAccessor.cs` — Actor 生成箇所を更新
4. `TagConsistentGrain.cs` — `DcbDomainTypes` → `ITagTypes`
5. `TagStateGrain.cs` — `DcbDomainTypes` → 3 個別インターフェース
6. DI 登録 — 3 つの Program.cs + テスト DI
7. テスト更新 — 7 テストファイルのコンストラクタ引数修正
8. ビルド・テスト確認
