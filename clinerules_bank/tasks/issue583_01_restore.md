
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

この行の上は削除しないでください。
ーーーーーーーーーーーーーーーーーーーーーーーー以下の行に追記可能-------------------



