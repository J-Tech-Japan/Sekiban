# AOT対応: WithoutResult用ドメインモデル分離設計

## 目的（want.mdの要件）
- AOT環境で **ResultBox を返さないドメイン定義** を書けるようにする。
- `Sekiban.Dcb.Core.Model` だけでは足りないため **WithoutResult 向けのModelプロジェクト** を用意する。
- 参照順位（依存関係）を整理し、循環参照を防ぐ。
- WithResult形式でも必要に応じて書けることを維持する。

---

## 現状整理と問題点
- `Sekiban.Dcb.Core.Model` に **ResultBoxベースの ICore* インターフェース**が入っている。
- WithoutResult向けインターフェース (`IMultiProjector`, `IMultiProjectionListQuery` など) は **runtimeパッケージ内に直書き**で、モデル専用プロジェクトがない。
- AOT向けの `AotDomainTypesBuilder` は ResultBox ベースのみ。
- AOTで WithoutResult の戻り値を使ったドメイン定義を書こうとすると、
  - runtime依存が混ざる
  - AOTビルドで必要な型の参照関係が不明確

---

## 設計方針（結論）
1. **ICore系は共用の中核として Core.Model に集約**する（追加で整理も可能）。  
2. **WithResult/WithoutResult それぞれの Model プロジェクトを新設**し、`IMulti*` などの“表面API”を置く。  
3. AOT向けに **WithoutResult版のAOTレジストリ（MultiProjector/Query）**を用意する。  
4. 参照順序を明示して循環参照を回避する。  

---

## 新しいプロジェクト構成（提案）

```
Sekiban.Dcb.Core.Model               (共通中核 / ICore系 / AOT基盤)
    └─ ICoreMultiProjector / ICoreQuery* / AotDomainTypesBuilder 等

Sekiban.Dcb.WithResult.Model         (ResultBoxベース表面API)
    ├─ IMultiProjector<T>
    ├─ IMultiProjectorWithCustomSerialization<T>
    ├─ IMultiProjectionQuery<...>
    └─ IMultiProjectionListQuery<...>

Sekiban.Dcb.WithoutResult.Model      (例外ベース表面API)
    ├─ IMultiProjector<T>
    ├─ IMultiProjectorWithCustomSerialization<T>
    ├─ IMultiProjectionQuery<...>
    ├─ IMultiProjectionListQuery<...>
    └─ AotWithoutResult* (新規)

Sekiban.Dcb.Core                     (runtime共通)
Sekiban.Dcb.WithResult               (runtime, ResultBox API)
Sekiban.Dcb.WithoutResult            (runtime, Exception API)
```

### 参照順序（依存関係）
```
Core.Model  ←  WithResult.Model     ←  WithResult(runtime)
Core.Model  ←  WithoutResult.Model  ←  WithoutResult(runtime)
Core.Model  ←  Core(runtime)
```
- **各 Model は Core.Model を参照**し、共通型（Event/Tags/DcbDomainTypes/ICore*）を使う。  
- runtime側は **Modelに依存**する（Model → runtime の逆依存は禁止）。  

---

## Model へ移すもの（WithResult/WithoutResult 共通方針）
以下は runtime から **移動** し、それぞれの Model に置く。

- WithResult.Model:
  - `MultiProjections/IMultiProjector.cs`
  - `MultiProjections/IMultiProjectorWithCustomSerialization.cs`
  - `Queries/IMultiProjectionQuery.cs`
  - `Queries/IMultiProjectionListQuery.cs`

- WithoutResult.Model:
  - `MultiProjections/IMultiProjector.cs`
  - `MultiProjections/IMultiProjectorWithCustomSerialization.cs`
  - `Queries/IMultiProjectionQuery.cs`
  - `Queries/IMultiProjectionListQuery.cs`

> NOTE: namespace は現状維持（`Sekiban.Dcb.MultiProjections` / `Sekiban.Dcb.Queries`）

### 互換性（API破壊対策）
- `Sekiban.Dcb.WithResult` / `Sekiban.Dcb.WithoutResult` 側に **TypeForwardedTo** を追加し、既存参照を壊さない。
- runtime の `using` は新 Model プロジェクトの型を使用する。

---

## AOT対応の追加設計
### 1) WithoutResult用 AOT MultiProjectorTypes
**目的**: AOTで `IMultiProjector<T>` を登録し、例外を ResultBox に包む。

提案クラス（新規）:
- `AotWithoutResultMultiProjectorTypes : ICoreMultiProjectorTypes`

主なAPI（案）:
```
RegisterProjector<TProjector>(JsonTypeInfo<TProjector> typeInfo)
    where TProjector : IMultiProjector<TProjector>, new();
```
内部では `TProjector.Project(...)` を直接呼び、例外を `ResultBox.Error` に変換する。

### 2) WithoutResult用 AOT QueryTypes
**目的**: `IMultiProjectionQuery` / `IMultiProjectionListQuery` を AOTで登録・実行。

提案クラス（新規）:
- `AotWithoutResultQueryTypes : ICoreQueryTypes`

登録API（案）:
```
RegisterQuery<TProjector, TQuery, TOutput>()
    where TQuery : IMultiProjectionQuery<TProjector, TQuery, TOutput>
```
```
RegisterListQuery<TProjector, TQuery, TOutput>()
    where TQuery : IMultiProjectionListQuery<TProjector, TQuery, TOutput>
```
内部では `TQuery.HandleQuery/HandleFilter/HandleSort` を直接呼び、例外を ResultBox に変換。

### 3) WithoutResult AOT DomainTypesBuilder
`AotDomainTypesBuilder` の WithoutResult版を追加:
- `AotDomainTypesBuilderWithoutResult`
- MultiProjectorTypes に `AotWithoutResultMultiProjectorTypes` を使用
- QueryTypes に `AotWithoutResultQueryTypes` を使用

---

## 使い方イメージ（AOT + WithoutResult）
```
var builder = new AotDomainTypesBuilderWithoutResult(jsonOptions);

builder.EventTypes.RegisterEvent<MyEvent>(MyJsonContext.Default.MyEvent);
builder.MultiProjectorTypes.RegisterProjector<MyProjector>(MyJsonContext.Default.MyProjector);
builder.QueryTypes.RegisterQuery<MyProjector, MyQuery, MyOutput>();

var domainTypes = builder.Build();
```

---

## ITagProjectorの扱い
- `ITagProjector` は **WithResult/WithoutResult共通**で問題なし。
- そのまま `Sekiban.Dcb.Core.Model` 側に残す。

---

## ICommand / ICommandHandler の移動（追加要件）
- AOT環境に限らず、**Command系もModelに集約**するのが理想。  
- 移動先は Core.Model（共通中核）を想定。  
- これにより runtime 依存を最小化し、Modelの再利用性とAOT適合性を高める。  

> 具体的な対象ファイルと依存先は別途棚卸しして決定（Command関連は現状 Core/WithResult/WithoutResult に分散の可能性があるため）。  

---

## 段階的導入プラン
### Phase 1（最小）
- WithoutResult.Model を新設
- runtime側からインターフェース移動
- 参照関係を整理

### Phase 2（AOT強化）
- AotWithoutResultMultiProjectorTypes / AotWithoutResultQueryTypes を追加
- AotDomainTypesBuilderWithoutResult を追加

---

## 期待効果
- AOT環境でも **ResultBox非依存のドメイン定義** が可能
- runtime と model の責務分離で **依存関係が明確化**
- WithResult/WithoutResult **両方の形式でドメイン定義可能**

---

## 保留・要確認
- `DcbDomainTypes` の型設計はそのままで良いか？
  - 現状は ICore* (ResultBox) 前提のため、WithoutResult側も内部では ResultBox で保持する想定
- AOT Query 実行の仕様（ReflectionなしでExecute可能か）
