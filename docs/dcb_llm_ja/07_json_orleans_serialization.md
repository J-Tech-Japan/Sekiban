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
> - [ストレージプロバイダー](11_dapr_setup.md)
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
