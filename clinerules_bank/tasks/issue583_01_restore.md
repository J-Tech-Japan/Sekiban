Orleasでstateを保存してリカバリする時にエラーになるのを回避したい。
State の保存に現状、System.Text.Jsonを使用していますが、以下のエラーとなっている。

```
{
"type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
"title": "Orleans.Runtime.OrleansException",
"status": 500,
"detail": "Error from storage provider AzureBlobGrainStorage.multiProjector during ReadStateAsync for grain multiprojector/AggregateListProjector1+MunicipalityProjector\n \nExc level 0: System.NotSupportedException: Deserialization of interface or abstract types is not supported. Type 'Sekiban.Pure.Projectors.IMultiProjectorCommon'. Path: $.ProjectorCommon | LineNumber: 0 | BytePositionInLine: 20.\n   at System.Text.Json.ThrowHelper.ThrowNotSupportedException(ReadStack& state, Utf8JsonReader& reader, Exception innerException)\n   at System.Text.Json.ThrowHelper.ThrowNotSupportedException_DeserializeNoConstructor(JsonTypeInfo typeInfo, Utf8JsonReader& reader, ReadStack& state)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter1.OnTryRead(Utf8JsonReader& reader, Type typeToConvert, JsonSerializerOptions options, ReadStack& state, T& value)\n at System.Text.Json.Serialization.JsonConverter1.TryRead(Utf8JsonReader& reader, Type typeToConvert, JsonSerializerOptions options, ReadStack& state, T& value, Boolean& isPopulatedValue)\n   at System.Text.Json.Serialization.JsonConverter1.TryReadAsObject(Utf8JsonReader& reader, Type typeToConvert, JsonSerializerOptions options, ReadStack& state, Object& value)\n at System.Text.Json.Serialization.Converters.LargeObjectWithParameterizedConstructorConverter1.ReadAndCacheConstructorArgument(ReadStack& state, Utf8JsonReader& reader, JsonParameterInfo jsonParameterInfo)\n   at System.Text.Json.Serialization.Converters.ObjectWithParameterizedConstructorConverter1.OnTryRead(Utf8JsonReader& reader, Type typeToConvert, JsonSerializerOptions options, ReadStack& state, T& value)\n at System.Text.Json.Serialization.JsonConverter1.TryRead(Utf8JsonReader& reader, Type typeToConvert, JsonSerializerOptions options, ReadStack& state, T& value, Boolean& isPopulatedValue)\n   at System.Text.Json.Serialization.JsonConverter1.ReadCore(Utf8JsonReader& reader, T& value, JsonSerializerOptions options, ReadStack& state)\n at System.Text.Json.Serialization.Metadata.JsonTypeInfo1.Deserialize(Utf8JsonReader& reader, ReadStack& state)\n   at System.Text.Json.JsonSerializer.ReadFromSpan[TValue](ReadOnlySpan1 utf8Json, JsonTypeInfo1 jsonTypeInfo, Nullable1 actualByteCount)\n at System.Text.Json.JsonSerializer.ReadFromSpan[TValue](ReadOnlySpan1 json, JsonTypeInfo1 jsonTypeInfo)\n at SystemTextJsonStorageSerializer.Deserialize[T](BinaryData data) in /Users/tomohisa/dev/GitHub/SR_BuildingAnalysis/src/BuildingAnalysis/MunicipalityManager.ApiService/SystemTextJsonStorageSerializer.cs:line 9\n at Orleans.Storage.AzureBlobGrainStorage.ConvertFromStorageFormat[T](BinaryData contents) in //src/Azure/Orleans.Persistence.AzureStorage/Providers/Storage/AzureBlobStorage.cs:line 345\n at Orleans.Storage.AzureBlobGrainStorage.ReadStateAsync[T](String grainType, GrainId grainId, IGrainState1 grainState) in /_/src/Azure/Orleans.Persistence.AzureStorage/Providers/Storage/AzureBlobStorage.cs:line 109\n   at Orleans.Core.StateStorageBridge1.ReadStateAsync() in //src/Orleans.Runtime/Storage/StateStorageBridge.cs:line 85\nExc level 1: System.NotSupportedException: Deserialization of interface or abstract types is not supported. Type 'Sekiban.Pure.Projectors.IMultiProjectorCommon'.",
"traceId": "00-45ac96e59ecb75dcbeb6950bb22d88a2-5ef4b7a0c53e1ddf-01"
}
```

保存するオブジェクト
src/Sekiban.Pure/Projectors/MultiProjectionState.cs
がIMultiProjectorCommon ProjectorCommon

を使用しているのが問題と考えられる

src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs

で保存するステート自体は、一般的に保存、リストアできるものにするべきです。

その変更をこのチケットで行いたい。

safeState で使用する、シリアライズできる型を
src/Sekiban.Pure.Orleans/Parts
に作成する。

名前は
SerializableMultiProjectionState

safeStateに保存するときは、MultiProjectionStateから、SerializableMultiProjectionStateに変換する

SerializableMultiProjectionStateは、IMultiProjectorCommon ProjectorCommonではなくて、
IMultiProjectorCommon ProjectorCommon をシリアライズしたJSON文字列かバイトコードを保持する。保存スピードを考えて、JSON文字列をGZipかその他簡単にアクセスできる一般の圧縮方法で圧縮したものが望ましい。

SerializableMultiProjectionState はには以下の機能をつける
MultiProjectionStateからSerializableMultiProjectionStateに変換する機能
SerializableMultiProjectionStateからMultiProjectionStateに変換する、しかしバージョンが合っていなかったら、変換に失敗したことがわかるように、OptionalValue<>型でNoneとすることにより、一から解析をやり直すことようにプログラムが理解でき、一から行うようにする。

IMultiProjectorCommonの機能は以下のインターフェースで共通定義をされていて
src/Sekiban.Pure/Projectors/IMultiProjectorsType.cs
この実装をSource Generator で生成している。

src/Sekiban.Pure/Projectors/IMultiProjectorsType.cs
に機能を追加したい場合、メソッドを追加したのち

src/Sekiban.Pure.SourceGenerator/MultiProjectorTypesGenerator.cs
ここで機能追加が可能です。

以上が行いたいことですが、設計を考えて、どのような変更を行うかの方針を具体的にこのファイルに追記してください。
clinerules_bank/tasks/issue583_01_restore.md
わからないことは質問してその答えを得てから進めてください。わからないことを勝手に進めても成功しません。

実装方針を書き込んだら一旦終了してください。レビューをしてよければ、実装方針の通り進めるようにこちらから依頼します。

この行の上は削除しないでください。
ーーーーーーーーーーーーーーーーーーーーーーーー以下の行に追記可能-------------------

## 実装方針案

Orleans GrainのState永続化において `System.Text.Json` がインターフェース (`IMultiProjectorCommon`) を直接シリアライズできない問題に対応するため、以下の実装方針を提案します。

1.  **`SerializableMultiProjectionState` の定義:**
    *   新しいファイル `src/Sekiban.Pure.Orleans/Parts/SerializableMultiProjectionState.cs` を作成します。
    *   このファイル内に新しい `record SerializableMultiProjectionState` を定義します。
    *   このレコードは、`System.Text.Json` で安全にシリアライズ可能な形式で状態を保持します。インターフェース型の直接保持を避けます。
    *   プロパティ構成:
        *   `byte[] CompressedPayloadJson`: 元の `MultiProjectionState<TProjection>.Payload` (TProjection インスタンス) をJSONシリアライズし、GZip圧縮したもの。
        *   `string PayloadTypeName`: `TProjection` 型のAssembly修飾名。デシリアライズ時の型検証に使用します。
        *   `string PayloadVersion`: `TProjection` を含むアセンブリのバージョン識別子 (例: AssemblyVersion)。型の構造変更を検出するために使用します。
        *   `byte[] CompressedAggregatesJson`: 元の `MultiProjectionState<TProjection>.Aggregates` ディクショナリをJSONシリアライズし、GZip圧縮したもの (`Aggregate<TProjection>` も `TProjection` を含むため)。
        *   `string AggregatesVersion`: `Aggregate<TProjection>` の構造に関するバージョン識別子 (例: AssemblyVersion)。
        *   `Guid LastEventId`: `MultiProjectionState` から直接コピー。
        *   `string LastSortableUniqueId`: `MultiProjectionState` から直接コピー。

2.  **シリアライズ/デシリアライズロジックの実装:**
    *   オブジェクトを圧縮JSON (`byte[]`) にシリアライズし、元に戻すための静的ヘルパーメソッドを実装します (例: `SerializableMultiProjectionState` 内または専用ヘルパークラス)。エラーハンドリングを含みます。
    *   `PayloadVersion` と `AggregatesVersion` を取得する戦略を定義します (関連する型を含むアセンブリの `Assembly.GetName().Version.ToString()` を使用することを提案)。
    *   `JsonSerializerOptions` を一貫して使用し、アプリケーションで既に設定されているオプション (例: `JsonSerializerContext` に関連するもの) を再利用します。

3.  **変換メソッドの実装:**
    *   `SerializableMultiProjectionState` 内に非同期静的ファクトリメソッドを作成します:
        `public static async Task<SerializableMultiProjectionState> CreateFromAsync<TProjection>(MultiProjectionState<TProjection> state, JsonSerializerOptions options) where TProjection : IMultiProjectorCommon`
        *   `Payload` と `Aggregates` のシリアライズと圧縮、型名とバージョンの取得、直接プロパティのコピーを行い、新しい `SerializableMultiProjectionState` を返します。
    *   `SerializableMultiProjectionState` 内に非同期インスタンスメソッドを作成します:
        `public async Task<OptionalValue<MultiProjectionState<TProjection>>> ToMultiProjectionStateAsync<TProjection>(JsonSerializerOptions options) where TProjection : IMultiProjectorCommon`
        *   `PayloadTypeName` を現在の `typeof(TProjection)` と比較検証します。
        *   `PayloadVersion` と `AggregatesVersion` を関連する型/アセンブリの現在のバージョンと比較検証します。
        *   チェックが通れば、`CompressedPayloadJson` と `CompressedAggregatesJson` の解凍とデシリアライズを試みます。
        *   全てのステップが成功すれば、`MultiProjectionState<TProjection>` を再構築し `OptionalValue.Some(...)` で返します。
        *   いずれかのチェックまたはデシリアライズが失敗した場合、保存された状態が互換性がないか破損していることを示す `OptionalValue<MultiProjectionState<TProjection>>.None` を返します。

4.  **`MultiProjectorGrain<TProjection>` のリファクタリング:**
    *   Grainの永続状態フィールドを `IPersistentState<MultiProjectionState<TProjection>>` から `IPersistentState<SerializableMultiProjectionState>` に変更します。
    *   **`OnActivateAsync` 内:**
        *   `_state.State` から `SerializableMultiProjectionState` を読み込みます。
        *   `ToMultiProjectionStateAsync<TProjection>` 変換メソッドを呼び出します。
        *   結果が `Some(state)` であれば、それをGrainのアクティブ状態変数 (`_multiProjectionState`) に設定します。
        *   結果が `None` であれば、警告ログを記録し、Grainのアクティブ状態を初期の空状態 (`MultiProjectionState<TProjection>.CreateInitialState(...)`) にリセットし、イベント履歴からのプロジェクション再構築ロジックをトリガーします。
    *   **`WriteStateAsync` (または同等の状態永続化ロジック) 内:**
        *   Grainの現在のアクティブな `_multiProjectionState` を取得します。
        *   `SerializableMultiProjectionState.CreateFromAsync(...)` を呼び出してシリアライズ可能な形式に変換します。
        *   結果を `_state.State` に設定します。
        *   `await _state.WriteStateAsync()` を呼び出して永続化します。

5.  **依存関係の考慮:**
    *   `System.IO.Compression` や `System.Reflection` (アセンブリバージョン取得用) の `using` 文を追加します。
    *   `JsonSerializerOptions` が正しく渡され、設定されていることを確認します。

この方針により、永続化される状態から問題のあるインターフェースを圧縮JSON表現に置き換えることで、中心的なシリアライズ問題を解決します。また、バージョンチェックを追加することで、時間経過に伴う構造変化に対応し、必要な場合に再構築をトリガーします。
