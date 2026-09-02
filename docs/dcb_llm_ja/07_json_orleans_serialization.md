# シリアライゼーションとドメイン型登録

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [コマンドワークフロー](06_workflow.md)
> - [シリアライゼーションとドメイン型登録](07_json_orleans_serialization.md) (現在位置)
> - [API実装](08_api_implementation.md)
> - [クライアントUI (Blazor)](09_client_api_blazor.md)
> - [Orleans構成](10_orleans_setup.md)
> - [ストレージプロバイダー](11_storage_providers.md)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

DCB では `DcbDomainTypes` による明示的な型登録が必須です。イベント/タグ/プロジェクター/クエリ/マルチプロジェクション/
JSON オプションを一括して管理します (`src/Sekiban.Dcb/DcbDomainTypes.cs`)。

## DcbDomainTypes の利用

```csharp
public static DcbDomainTypes GetDomainTypes() =>
    DcbDomainTypes.Simple(types =>
    {
        types.EventTypes.RegisterEventType<StudentCreated>();
        types.TagStatePayloadTypes.RegisterPayloadType<StudentState>();
        types.TagProjectorTypes.RegisterProjector<StudentProjector>();
        types.TagTypes.RegisterTagGroupType<StudentTag>();
        types.MultiProjectorTypes.RegisterProjector<WeatherForecastProjection>();
        types.QueryTypes.RegisterListQuery<GetStudentListQuery>();
    });
```

### JSON オプション

- 既定は camelCase + 非インデント。
- `jsonOptions` 引数でカスタム `JsonSerializerOptions` を指定可能。
- イベントストアも同じオプションでシリアライズするため、サービス間で統一してください。

## Orleans のシリアライズ

Orleans の Source Generator を利用するため、タグ状態やクエリ結果には `[GenerateSerializer]` を付与します。
(`internalUsages/Dcb.Domain/Student/StudentState.cs` など)

イベントペイロードは `IEventStore` が System.Text.Json でシリアライズしますが、互換性維持のため
`Sekiban.Dcb.Orleans` には `NewtonsoftJsonDcbOrleansSerializer` も用意されています。

## イベントメタデータと SortableUniqueId

永続化時には `SerializableEvent` にラップされ、ペイロード名とメタデータが保存されます。
`SortableUniqueId` (`src/Sekiban.Dcb/Common/SortableUniqueId.cs`) は UTC Ticks + 乱数で構成され、昇順を保証します。

## タグのシリアライズ

タグは `"Group:Content"` の文字列に変換されます。`ITag` または補助インターフェースを実装し、
逆変換 (`FromContent`) が可能なように設計してください。

## JSON コンテキストの拡張

バリューオブジェクトなど特別な変換が必要な場合は、`JsonSerializerOptions` にコンバーターを登録し、
`DcbDomainTypes` へ渡します。

## バージョン管理

- `ProjectorVersion`: タグプロジェクターのロジック変更時に更新。
- `MultiProjectorVersion`: 読み取りモデルのスキーマ変更時に更新。
- JSON 契約: 互換性が必要なら型名にバージョンを付与 (例: `WeatherForecastCountResultV2`)。

## トラブルシューティング

- 型未登録: "Event type not registered" などの例外 → `DomainType` へ登録漏れがないか確認。
- JSON 例外: ペイロード名 (`EventMetadata.EventType`) をログに出し、シリアライズ対象を特定。
- Orleans で新しい `[GenerateSerializer]` 型を追加した場合は完全ビルドを実行。

## Serialized Commit ワイヤ契約 (SEK-G17)

WASM 境界で使用される公式 serialized-commit ワイヤ契約の正規仕様です。

### 正規オーナー

契約は `Sekiban.Dcb.Core` パッケージの以下の型が所有します。

- `Sekiban.Dcb.Commands.SerializedCommitRequest` — リクエストエンベロープ (positional record)。
- `Sekiban.Dcb.Events.SerializableEventCandidate` — 1 件のイベント候補。
- `Sekiban.Dcb.Commands.ConsistencyTagEntry` — 1 件の整合性予約エントリ。
- `Sekiban.Dcb.Actors.ISerializedSekibanDcbExecutor.CommitSerializableEventsAsync` — 受理オペレーション。

この契約に準拠すると主張するエンドポイントは、以下の形状・シリアライザ設定、および凍結されたゴールデンベクタ
(`SerializedCommitWireGoldenTests`) に必ず適合しなければなりません。

### JSON 形状

```json
{
  "eventCandidates": [
    {
      "payload": "<イベントペイロード JSON の UTF-8 バイト列の base64>",
      "eventPayloadName": "<登録済みイベント型名>",
      "tags": ["Group:Content", "..."]
    }
  ],
  "consistencyTags": [
    { "tag": "Group:Content", "lastSortableUniqueId": "<sortable-unique-id もしくは \"\">" }
  ]
}
```

| フィールド | 型 | 必須 | 補足 |
| --- | --- | --- | --- |
| `eventCandidates` | array | はい (空可) | 順序付き。各要素が順番に 1 イベントとして書き込まれます。空配列は有効な「空コミット」です。 |
| `eventCandidates[].payload` | string | はい | イベントペイロード JSON の UTF-8 バイト列の base64。コミット経路にとって不透明 (そのまま保存)。 |
| `eventCandidates[].eventPayloadName` | string | はい | ペイロードの解決・検証に用いる登録済みイベント型名。 |
| `eventCandidates[].tags` | string[] | はい (空可) | **イベント単位のタグが正 (authoritative)**。各イベントは自身のタグリストを保持し、イベント間で平坦化・共有されません。形式は `"Group:Content"`。 |
| `consistencyTags` | array | はい (空可) | 楽観的同時実行の予約。ここの各 `tag` はいずれかのイベント候補の `tags` にも存在する必要があります。 |
| `consistencyTags[].tag` | string | はい | `"Group:Content"`。 |
| `consistencyTags[].lastSortableUniqueId` | string | はい | 予約に対する直近の `SortableUniqueId`。空文字列は AssertEmpty。`null` は executor/store I/O より前に拒否されます。serialized 予約が不要なら consistency-tag entry 自体を省略します。typed command の Unspecified parity は legacy/V1 の対象外です。 |

### シリアライザ正規化 (完全固定)

本番ワイヤバイトは、契約所有のシリアライザ `Sekiban.Dcb.Commands.SerializedCommitWireContract.Options`
(ソース生成 `SerializedCommitWireJsonContext` に基づく) で固定されます。設定は以下のとおりです。

- **プロパティ命名**: camelCase。
- **プロパティ順序**: 宣言 / コンストラクタ引数順。
- **インデント**: なし。UTF-8、BOM なし、無意味な空白なし。
- **エンコーダ**: `JavaScriptEncoder.Default` — 非 ASCII および HTML 敏感文字は `\uXXXX` エスケープ (ASP.NET
  `JsonSerializerDefaults.Web` の書き込み経路とバイト単位で一致)。
- **null/既定値の扱い**: 常に出力 (無視しない)。
- **`byte[]` ペイロード**: base64 文字列。

固定は **追加のみ** です。ソース生成コンテキストに存在し、既存 positional DTO への属性ではありません。DTO への
シリアライズ属性の付与は禁止です。基準的に無害な `[JsonPropertyName]` ですら、新規 `JsonSerializerOptions`
(なお PascalCase を出力) でシリアライズする利用者の出力を変えてしまいます。ゴールデンベクタは契約シリアライザの
バイト列と fresh-options の PascalCase バイト列の両方を凍結するため、いかなるドリフトも CI で失敗します。

### 追加のバージョン付きエンベロープ + 二段階受理

公式形状にはバージョン識別子がありません。SEK-G17 はレガシー形状に一切触れずにバージョン付きエンベロープを追加します。

- `Sekiban.Dcb.Commands.VersionedSerializedCommitRequest(int Version, IReadOnlyList<SerializableEventCandidate>
  EventCandidates, IReadOnlyList<ConsistencyTagEntry> ConsistencyTags)`、`CurrentVersion = 1`。(G15 の単一イベント
  `SerializedConditionalCommitRequest` は基底エンベロープとして意図的に再利用しません。)

受理は `ISerializedCommitAcceptor` / `SerializedCommitAcceptor` による任意かつ追加的なものです (既存インターフェイスへの
メンバ追加はありません)。二段階です。

1. **フェーズ 1 — 生の識別と形状ゲート** (`SerializedCommitVersionDiscriminator`): `version` プロパティとトップレベルの
   collection-member 形状を、型付きペイロードのバインド・base64 デコード・タグ予約・EventId 採番・executor/store 呼び出しの前に、
   生の UTF-8 バイトから読み取ります。識別子は **厳密な** 序数プロパティ名 `version` (契約の camelCase 表記) です。照合は意図的に
   **大文字小文字を区別** し、周囲の大文字小文字非依存は一切使いません。
   - `version` もその大小文字違いも無し → レガシー経路。
   - 厳密な整数 `version` が 1 → 既知バージョン。
   - 厳密な整数 `version` が 1 以外 → **`UnsupportedSerializedCommitEnvelopeVersionException`** (副作用の前に fail closed)。
   - `version` の **大小文字違い** (例: `Version` / `VERSION` / `vErSiOn`) は、単独でも厳密版と併存でも、V1 やレガシーとして
     暗黙選択されず、**`MalformedSerializedCommitException`** (`AmbiguousVersionCasing`) です。
   - 非オブジェクトのルート、非整数 `version`、厳密な `version` の重複 → **`MalformedSerializedCommitException`** (別種の
     型付き shape エラー)。この型付きエラーは **秘密安全** で、閉じた理由コードと固定メッセージのみを持ち、問題の JSON・
     キー・ペイロード/base64・型名・生のパーサ例外を一切含みません。
   - legacy/V1/V2 のすべてで `eventCandidates` と `consistencyTags` はそれぞれ厳密に 1 回必要です。V2 では
     `expectedTagPositions` も厳密に 1 回必要であり、conditional write が unconditional write に黙って変わらないよう、
     この V2 専用 member は legacy/V1 では拒否されます。`candidates` と `consistency` はフォールバック名ではなく拒否対象の
     alias です。
2. **フェーズ 2 — バインド + ルーティング**: 解決された形状のみをバインドします。version 欠如はレガシー公式形状であり、
   `LegacyUnversionedSerializedCommitAdapter` が V1 へ無損失にリフトします (イベント単位のタグを保持、per-commit-tag
   モデルは介在しません)。既知バージョンは `VersionedSerializedCommitRequest` をバインドします。バインド失敗 (不正な V1
   ペイロードを含む) は型付き `MalformedSerializedCommitException` として報告され、null 参照にはなりません。いずれの
   経路も同じイベント候補 + 整合性タグを `ISerializedSekibanDcbExecutor.CommitSerializableEventsAsync` へ同一セマンティクス
   でルーティングします。

### 生の形状マトリクス (SEK-G51)

このゲートは strict-schema migration ではありません。無関係なトップレベル拡張 member は許容し、欠如・alias・曖昧さによって
空の成功コミットとしてデシリアライズされ得る protocol name だけを保護します。

| 生のトップレベル形状 | Legacy | V1 | V2 | 結果 |
| --- | --- | --- | --- | --- |
| `eventCandidates` + `consistencyTags` が各 1 回。V2 は `expectedTagPositions` も持つ | 許可 | 許可 | 許可 | 通常どおり bind + route。明示的な空配列ペアは空コミットとして成功します。 |
| 必須 member の片方または両方が欠如 | 拒否 | 拒否 | 拒否 | 副作用前に固定メッセージの `MalformedSerializedCommitException`。 |
| `candidates` / `consistency` が単独または公式名と混在 | 拒否 | 拒否 | 拒否 | Alias dialect を黙って無視しません。 |
| 公式名 (`eventCandidates`、`consistencyTags`、`expectedTagPositions`) の重複または大小文字違い | 拒否 | 拒否 | 拒否 | 曖昧な protocol shape は fail closed。 |
| 完全な公式形状 + `x-trace` のような無関係 member | 許可 | 許可 | 許可 | 契約 binder は extension member を無視します。 |

`{"eventCandidates":[],"consistencyTags":[]}` は意図的に許可され、`{"consistencyTags":[]}` は意図的に拒否されます。
**SekibanWasmRuntime** などの consumer はこのゲートを含む release を取り込み、自身の request binder を個別に証明する必要があります。
このパッケージだけでは downstream binder の欠如配列 coalesce を停止できません。

### 所有と互換性主張のガイダンス

- 公式契約 (`eventCandidates` + base64 `payload` + `eventPayloadName` + **イベント単位の `tags`** + `consistencyTags`) は
  dcb-v10.2.2 → 10.6.0 で安定しており、10.1.x のプログラムに移行は不要です。
- 別の `events` / `payloadJson` / per-commit-`tags` 形状は、独立した下流ランタイム契約 (例: WASM ランタイムや
  as-a-service ホスト) です。本契約ではなく、本契約を写したものと説明してはいけません。
- 本契約との互換性を主張するエンドポイントは、上記仕様に適合しゴールデンベクタを通過しなければなりません。
- イベント単位のタグをコミット単位のタグへ縮約する下流アダプタは、コミット内の全イベントが同一のタグ集合を持つ場合に
  限り許され、それ以外のコミットは黙ってタグを落とさず明示的に拒否しなければなりません。
