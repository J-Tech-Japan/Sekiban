# カスタム MultiProjector シリアライゼーション

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [コマンドワークフロー](06_workflow.md)
> - [シリアライゼーションとドメイン型登録](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントUI (Blazor)](09_client_api_blazor.md)
> - [Orleans構成](10_orleans_setup.md)
> - [ストレージプロバイダー](11_dapr_setup.md)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)
> - [カスタム MultiProjector シリアライゼーション](17_custom_multiprojector_serialization.md) (現在位置)

MultiProjector の状態は大きくなりがちです。カスタムシリアライゼーションを使用すると、
ストレージ形式を最適化し、圧縮を制御してパフォーマンスを向上させることができます。

## SerializationResult レコード

カスタムシリアライゼーションを実装する場合、`Serialize` メソッドは `SerializationResult` を返します:

```csharp
public record SerializationResult(
    byte[] Data,              // シリアライズ済みデータ（圧縮有無はシリアライザ次第）
    long OriginalSizeBytes,   // 圧縮前のサイズ
    long CompressedSizeBytes  // 圧縮後のサイズ（非圧縮の場合は OriginalSizeBytes と同じ）
)
{
    public double CompressionRatio => OriginalSizeBytes > 0
        ? (double)CompressedSizeBytes / OriginalSizeBytes
        : 1.0;
}
```

## インターフェース: ICoreMultiProjectorWithCustomSerialization<T>

マルチプロジェクターにカスタムシリアライゼーションを定義するには、このインターフェースを実装します:

```csharp
public interface ICoreMultiProjectorWithCustomSerialization<T> : ICoreMultiProjector
    where T : ICoreMultiProjectorWithCustomSerialization<T>, new()
{
    static abstract SerializationResult Serialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        T payload);

    static abstract T Deserialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        ReadOnlySpan<byte> data);
}
```

## 登録

`RegisterProjectorWithCustomSerialization<T>()` を使用してカスタムシリアライザを登録します:

```csharp
public static DcbDomainTypes GetDomainTypes() =>
    DcbDomainTypes.Simple(types =>
    {
        // 標準登録（デフォルトの JSON + Gzip を使用）
        types.MultiProjectorTypes.RegisterProjector<SimpleProjector>();

        // カスタムシリアライゼーション登録
        types.MultiProjectorTypes.RegisterProjectorWithCustomSerialization<OptimizedProjector>();
    });
```

## 圧縮ありの実装例

大きなペイロードには Gzip 圧縮を使用します:

```csharp
public record CounterProjector(int Count)
    : ICoreMultiProjectorWithCustomSerialization<CounterProjector>
{
    public static string MultiProjectorName => "CounterProjector";
    public static int MultiProjectorVersion => 1;

    public static SerializationResult Serialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        CounterProjector payload)
    {
        if (string.IsNullOrWhiteSpace(safeWindowThreshold))
            throw new ArgumentException("safeWindowThreshold must be supplied");

        var json = JsonSerializer.Serialize(
            new { v = 1, count = payload.Count },
            domainTypes.JsonSerializerOptions);
        var rawBytes = Encoding.UTF8.GetBytes(json);
        var originalSize = rawBytes.LongLength;
        var compressed = GzipCompression.Compress(rawBytes);
        var compressedSize = compressed.LongLength;

        return new SerializationResult(compressed, originalSize, compressedSize);
    }

    public static CounterProjector Deserialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        ReadOnlySpan<byte> data)
    {
        var rawBytes = GzipCompression.Decompress(data.ToArray());
        var json = Encoding.UTF8.GetString(rawBytes);
        var obj = JsonSerializer.Deserialize<JsonObject>(json, domainTypes.JsonSerializerOptions);
        var count = obj?["count"]?.GetValue<int>() ?? 0;
        return new CounterProjector(count);
    }

    // ... Project メソッド
}
```

## 圧縮なしの実装例

小さなペイロードには、高速なシリアライゼーションのために圧縮をスキップします:

```csharp
public record SmallProjector(string Value)
    : ICoreMultiProjectorWithCustomSerialization<SmallProjector>
{
    public static string MultiProjectorName => "SmallProjector";
    public static int MultiProjectorVersion => 1;

    public static SerializationResult Serialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        SmallProjector payload)
    {
        var json = JsonSerializer.Serialize(payload, domainTypes.JsonSerializerOptions);
        var rawBytes = Encoding.UTF8.GetBytes(json);
        var size = rawBytes.LongLength;

        // 圧縮なし: OriginalSize = CompressedSize
        return new SerializationResult(rawBytes, size, size);
    }

    public static SmallProjector Deserialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        ReadOnlySpan<byte> data)
    {
        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<SmallProjector>(json, domainTypes.JsonSerializerOptions)!;
    }
}
```

## データフロー

```
┌─────────────────────────────────────────────────────────────────────┐
│                  SimpleMultiProjectorTypes.Serialize                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  カスタムシリアライザあり?                                            │
│  ├─ Yes → T.Serialize(domain, threshold, payload)                   │
│  │         → SerializationResult をそのまま返す                      │
│  │         (圧縮有無・方式はカスタム側で完全制御)                      │
│  │                                                                  │
│  └─ No (Fallback)                                                   │
│       1. JSON シリアライズ → rawBytes                               │
│       2. OriginalSize = rawBytes.Length                             │
│       3. Gzip 圧縮 → compressed                                     │
│       4. CompressedSize = compressed.Length                         │
│       5. SerializationResult(compressed, Original, Compressed)      │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                 SimpleMultiProjectorTypes.Deserialize               │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  カスタムシリアライザあり?                                            │
│  ├─ Yes → T.Deserialize(domain, threshold, data)                    │
│  │         → データをそのまま渡す                                    │
│  │         (解凍有無・方式はカスタム側で完全制御)                      │
│  │                                                                  │
│  └─ No (Fallback)                                                   │
│       1. Gzip 解凍 → rawBytes                                       │
│       2. JSON デシリアライズ → payload                               │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

## バージョニング

シリアライゼーション形式や圧縮方式を変更する場合:

1. プロジェクターの `MultiProjectorVersion` をインクリメント
2. システムは古いスナップショットを読み込まず、イベントから再構築
3. 古い保存状態は自動的に新しい形式に置き換えられる

```csharp
public static int MultiProjectorVersion => 2; // 形式変更時にバンプ
```

## オフロード閾値

`CompressedSizeBytes` はオフロード判定（大きな状態を Blob Storage に移動するなど）に使用されます。
適切な閾値計算のために、`SerializationResult` で正確なサイズを報告してください。

## 移行手順

シリアライゼーション形式が変更されるバージョンにアップグレードする場合:

- **Postgres**: `DELETE FROM "MultiProjectionStates";`
- **Cosmos DB**: MultiProjectionStates コンテナのドキュメントを削除
- **Orleans Grain State**: 自動的に再構築

システムは最初のアクセス時にイベントから状態を再構築します。

## ベストプラクティス

1. **圧縮**: 1KB を超えるペイロードには Gzip を使用、小さいものはスキップ
2. **バージョニング**: シリアライズ形式にバージョン番号を含める
3. **safeWindowThreshold**: このパラメータは常に検証; シリアライゼーションスコープを定義
4. **エラーハンドリング**: シリアライゼーション失敗時は例外をスロー; システムが `ResultBox` でラップ
5. **テスト**: シリアライゼーションの往復とバージョンアップグレードの両方をテスト
