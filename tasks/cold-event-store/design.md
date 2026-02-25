# Cold Event Store 設計（実装ドラフト）

## フェーズ別詳細

- [Phase 1 設計](./phase1.md)
- [Phase 2 設計](./phase2.md)
- [Phase 3 設計](./phase3.md)

## 目的

- Hot Store（Cosmos DB / DynamoDB など）への連続読取負荷を下げつつ、キャッチアップを高速化する。
- 既存経路に影響を与えないよう、機能をデフォルト無効で導入する。
- JSONL / DuckDB / SQLite いずれの実装にも共通する最小インターフェースを先に固定する。

## 要件整理（今回反映する内容）

- 非対応を返せるインターフェースを用意する。
- デフォルトは「使用不可（false）」とし、既定ではコールドストアを使わない。
- 安全な更新方式として Pull 型（例: 30分ごと）を第一候補にする。
- Change Feed / Stream 方式も可能だが、Safe Window（1-2分）未満のデータは取り込まない設計を含める。
- データは 100,000 または 1,000,000 イベント単位で分割保存できるようにする。
- データ本体ファイルとは別に「どこまで保存済みか」を示す管理ファイルを持つ。
- 管理ファイルのみ取得して現状把握できるようにする。
- Blob/S3 での同時書き込み制御（競合対策）を含める。

## 全体構成

```text
[Hot Event Store]
   -> (Pull Export Job: 30分周期推奨)
[Cold Exporter]
   -> segment files (jsonl/duckdb/sqlite)
   -> control files (manifest/checkpoint/leases)
[Blob or S3]

[Cold Reader]
   1) control fileだけ読む
   2) 必要segmentを決定
   3) segment本体を読む
```

## 管理ファイル分離

### 1. segment 本体ファイル

- 例:
  - `segments/{serviceId}/{generation}/{segmentNo}.jsonl.zst`
  - `segments/{serviceId}/{generation}/{segmentNo}.duckdb`
  - `segments/{serviceId}/{generation}/{segmentNo}.sqlite`
- 不変（immutable）運用。
- 1セグメントの最大イベント数を設定可能。

### 2. control ファイル

- `control/{serviceId}/manifest.json`
  - 保存済み範囲、セグメント一覧、最新安全位置を保持
- `control/{serviceId}/checkpoint.json`
  - エクスポータが次回開始する `nextSince` を保持
- `control/{serviceId}/lease.json`（またはロック専用メカニズム）
  - 同時書き込み回避に使用

`manifest.json` だけを取得すれば、データ本体を読まずに現在位置を判定できる。

## インターフェース案

以下は DCB の実装向けドラフト。ポイントは「非対応」「デフォルト無効」を明示すること。

```csharp
namespace Sekiban.Dcb.ColdEvents;

public record ColdEventStoreOptions
{
    // デフォルト無効
    public bool Enabled { get; init; } = false;

    // Pull 型エクスポート間隔（推奨: 30分）
    public TimeSpan PullInterval { get; init; } = TimeSpan.FromMinutes(30);

    // Safe Window: この時間以内のイベントは取り込まない
    public TimeSpan SafeWindow { get; init; } = TimeSpan.FromMinutes(2);

    // セグメント閾値（どちらか先に到達したらローテーション）
    public int SegmentMaxEvents { get; init; } = 100_000; // 1_000_000に変更可能
    public long SegmentMaxBytes { get; init; } = 512L * 1024 * 1024;
}

public record ColdFeatureStatus(
    bool IsSupported,
    bool IsEnabled,
    string Reason);

public record ColdStoreProgress(
    string ServiceId,
    string? LatestSafeSortableUniqueId,
    string? LatestExportedSortableUniqueId,
    string? NextSinceSortableUniqueId,
    DateTimeOffset? LastExportedAtUtc,
    string ManifestVersion);

public interface IColdEventStoreFeature
{
    // 非対応を返せる（NotSupported実装で IsSupported=false）
    Task<ColdFeatureStatus> GetStatusAsync(CancellationToken ct = default);
}

public interface IColdEventProgressReader : IColdEventStoreFeature
{
    // データ本体を読まずに状況把握
    Task<ResultBox<ColdStoreProgress>> GetProgressAsync(
        string serviceId,
        CancellationToken ct = default);
}

public interface IColdEventExporter : IColdEventStoreFeature
{
    // Pullで増分取得しsegmentを追加
    Task<ResultBox<ExportResult>> ExportIncrementalAsync(
        string serviceId,
        CancellationToken ct = default);
}

public interface IColdEventReader : IColdEventStoreFeature
{
    Task<ResultBox<IReadOnlyList<SerializableEvent>>> ReadAllSerializableEventsAsync(
        string serviceId,
        SortableUniqueId? since = null,
        int? maxCount = null,
        CancellationToken ct = default);
}
```

### 非対応デフォルト実装

```csharp
public sealed class NotSupportedColdEventStore :
    IColdEventProgressReader,
    IColdEventExporter,
    IColdEventReader
{
    public Task<ColdFeatureStatus> GetStatusAsync(CancellationToken ct = default)
        => Task.FromResult(new ColdFeatureStatus(
            IsSupported: false,
            IsEnabled: false,
            Reason: "Cold event store is not configured"));

    public Task<ResultBox<ColdStoreProgress>> GetProgressAsync(string serviceId, CancellationToken ct = default)
        => Task.FromResult(ResultBox.Error<ColdStoreProgress>(
            new NotSupportedException("Cold event store is not supported")));

    public Task<ResultBox<ExportResult>> ExportIncrementalAsync(string serviceId, CancellationToken ct = default)
        => Task.FromResult(ResultBox.Error<ExportResult>(
            new NotSupportedException("Cold event store is not supported")));

    public Task<ResultBox<IReadOnlyList<SerializableEvent>>> ReadAllSerializableEventsAsync(
        string serviceId,
        SortableUniqueId? since = null,
        int? maxCount = null,
        CancellationToken ct = default)
        => Task.FromResult(ResultBox.Error<IReadOnlyList<SerializableEvent>>(
            new NotSupportedException("Cold event store is not supported")));
}
```

## 更新方式設計

## A. Pull 型（推奨・初期実装）

- 実行トリガー: Function / Cron / Worker（30分ごと）
- フロー:
  1. lease取得
  2. `checkpoint.nextSince` から Hot Store 増分取得
  3. `now - SafeWindow` より新しいイベントを除外
  4. segment閾値でファイルを切る
  5. segmentアップロード
  6. manifest/checkpointを条件付き更新
  7. lease解放

利点:

- 実装が単純
- 障害復旧しやすい
- セーフウィンド制御を入れやすい

## B. Stream/Change Feed 型（将来拡張）

- 低遅延だが、再送/順序/重複制御が複雑。
- Safe Window 内イベントの遅延確定が必要。
- 初期は採用せず、Pull 実績後にオプション化する。

## Safe Window の扱い

- 基準時刻 `cutoff = UtcNow - SafeWindow`。
- `SortableUniqueId.GetDateTime()` の時刻が `cutoff` 以前のイベントのみエクスポート対象。
- 既定は 2分。環境により 1分に短縮可能。

## セグメント分割方式

- ローテーション条件（OR）
  - `SegmentMaxEvents` 到達（100,000 または 1,000,000）
  - `SegmentMaxBytes` 到達
- セグメントは不変。
- 再実行時の重複対策として、manifest 登録時に `sha256` と範囲（from/to）を検証する。

## 同時書き込み制御（Blob/S3）

## 課題

- 管理ファイル（manifest/checkpoint）同時更新時のロストアップデート。

## 対策

- 単一ライター原則 + 条件付き更新。
- `lease` と `manifest` 更新は以下で実施:
  - Azure Blob: Lease + ETag (`If-Match`)
  - S3: 条件付き書き込み（ETag比較）または DynamoDB ロック併用
- 世代番号 `manifestVersion` を持ち、更新時に `expectedVersion` 一致を必須にする。
- 不一致時は失敗させ、再取得してリトライ。

## 運用時の取得手順（Reader）

1. `manifest.json` のみ取得
2. `LatestSafeSortableUniqueId` と segment index を確認
3. 必要範囲の segment のみダウンロード
4. 末尾不足分のみ Hot Store を読む（Hybrid構成時）

## DI / 既定動作

- 既定登録:
  - `IColdEventProgressReader`, `IColdEventExporter`, `IColdEventReader` に `NotSupportedColdEventStore` を登録
  - `ColdEventStoreOptions.Enabled = false`
- 有効化時のみ実装を差し替える。

これにより、未設定環境では必ず「非対応」または「無効」が返る。

## 実装フェーズ

### Phase 1

- インターフェース + NotSupported 実装
- JSONL segment + manifest/checkpoint + lease
- 30分 Pull Export ジョブ

### Phase 2

- Hybrid Reader（Cold + Hot tail）
- 監視メトリクス（latest safe lag / export duration / conflict retries）

### Phase 3

- DuckDB/SQLite 派生生成
- Change Feed/Stream オプション実装

## 検証項目

- Enabled=false で既存機能に影響しないこと
- 同時実行2ジョブで manifest 競合時に整合性が壊れないこと
- Safe Window 内イベントが取り込まれないこと
- 100,000 / 1,000,000 閾値で正しく segment 分割されること
- manifest 単体取得で保存済み位置が取得できること
