# Sekiban DCB 11.0.1-preview01

- バージョン: `11.0.1-preview01`。DCB 11 系の最初のプレビューです。DCB ライブラリは 26 パッケージです。
- 対象は **net10.0 と net11.0** です（net9.0 は対象外です。net9.0 のプロジェクトは 10.24.x を使ってください）。C# 15 と .NET 11 SDK `11.0.100` でビルドしています。net10.0 で使う場合、.NET 11 SDK は不要です。
- **ResultBox を使うのは WithResult の表面だけになりました。** 10.25.0 の公開 API に対する破壊的変更を含みます。11.0.1-preview01 に対して再コンパイルし、移行ガイド `docs/dcb_llm_ja/24_migrating_to_11.md`（英語版は `docs/dcb_llm/24_migrating_to_11.md`）を読んでください。
- DCB のイベントデータの移行は不要です。イベント、タグ、スナップショット、永続化された Orleans の grain 状態は変わっていません。

## ResultBox は WithResult の表面だけに

`ResultBoxes` に依存するのは `Sekiban.Dcb.WithResult`、`Sekiban.Dcb.WithResult.Model`、`Sekiban.Dcb.Orleans.WithResult`、`Sekiban.Dcb.WithResult.Testing` だけです。Core、Core.Model、Core.Testing、すべてのストレージプロバイダー、Orleans.Core、MaterializedView、WithoutResult 系のパッケージは例外ベースになりました。変換は次の 4 つのルールに従っています。

1. 失敗は、これまでと同じ型・同じメッセージの例外になります。
2. 想定内の結果は値で返します（`null`・`false`・空文字列・空の状態）。イベントやスナップショットが見つからない場合などです。
3. キャンセルは引き続き `OperationCanceledException` です。
4. 契約は形だけが変わります。`ResultBox<T>` が `T` になり、名前と引数はそのままです。

10.25.0 に対する意図的な API の破壊的変更は、すべて `dcb/src/` の各パッケージの `CompatibilitySuppressions.xml` に記録しています。

## 影響を受ける方

- **WithResult を使っている方。** 書くための API は変わりません。コマンドハンドラー、プロジェクター、クエリ、`ICommandContext`、`ISekibanExecutor` は、これまでどおり `ResultBox` を返します。Orleans の grain から `[TypeName] message` の形に平坦化されて届いていた失敗の文言は、その接頭辞が付かなくなることがあります（"Failed to reserve tags: ..." に含まれるタグごとの文言など）。ソースの変更が届く場合もあります。`SortableUniqueId.Parse` は値を返して失敗時に `ArgumentException` を送出し、`ToTypedResult<T>()` は値を返し、`ConsistencyTag.SortableUniqueId` は null 許容になり、`ICommandContext` は `ICoreCommandContext` を継承しなくなりました。コアの契約を直接呼ぶコードは値を受け取ります。
- **WithoutResult を使っている方。** `GeneralSekibanExecutor` のシリアライズ済みメンバー（`GetSerializableTagStateAsync`、`CommitSerializableEventsAsync`、`CommitSerializableEventsWithExpectedTagPositionsAsync`、`CommitSerializableEventConditionallyAsync`）と、`OrleansDcbExecutor` の最初の 3 つは、`Task<ResultBox<T>>` ではなく `Task<T>` を返します。`SimpleMultiProjectorTypes.RegisterProjectorWithCustomSerialization<T>()` は `bool` を返し、失敗すると元の例外を送出します。WithoutResult は `ResultBoxes` を推移的に持ち込まなくなったので、使っている場合は自分で参照してください。失敗の型と、`Sekiban.Boundary.Operation` / `Sekiban.Boundary.Target` のデータキーは変わりません。
- **独自ストレージプロバイダー・`IEventStore` を実装している方。** すべてのストレージ契約が値を返し、失敗を送出します。対象は `IEventStore`、`IMultiProjectionStateStore`、`IProjectionStatusStore`、`IGenerationAwareCheckpointStore`、`IConditionalEventStore`、`IExpectedTagPositionEventStore`、ストリーミングのストア、コールドストレージの契約、`IDurableSubscriptionStore` です。見つからない読み取り（`ReadSerializableEventAsync`、`GetLatestForVersionAsync`、`GetLatestAnyVersionAsync`、`IColdObjectStorage.GetAsync`）は `null` を、存在しないものの削除は `false` を返し、`ReadCheckpointSlotAsync` は `CheckpointSlot.Absent` を返します。
- **独自の `I*Types` を実装している方。** `ICoreMultiProjectorTypes`、`IStreamingMultiProjectorTypes`、`ITagProjectorTypes`、`ITagStatePayloadTypes`、`ICoreQueryTypes` と、静的なコア契約（`ICoreMultiProjector<T>.Project`、`ICoreMultiProjectionQuery.HandleQuery`、`ICoreMultiProjectionListQuery.HandleFilter` / `HandleSort`、`ICoreCommandContext`）は値を返します。登録されていない名前は送出になります。
- **Orleans クラスター。** クラスター全体の再起動が必要です。grain インターフェイス（`IMultiProjectionGrain` と、メンバー `TryGetLatestSortableUniqueIdAsync` が増えた `ITagConsistentGrain`）が変わったため、10.x と 11 の silo やクライアントは同じクラスターで動きません。永続化された grain 状態、スナップショット、イベントは変わっていません。独自の `IProjectionActorHost` は値を返します。
- **例外の転送。** Sekiban の衝突・in-doubt・キー再利用・タグヘッド検証の例外に Orleans のコンバーターを追加し、以前箱を返していた grain メンバーは、Orleans がシリアライズできる失敗をそのまま送り、呼び出し側は失敗したタスクを受け取ります。コーデックがあるところでは `[TypeName]` の接頭辞は付かなくなりました。コーデックのない型や、Sekiban のコンバーターがなく `Data` に単純でない値を持つ例外は、これまでどおり `Exception("[TypeName] message")` として届きます。5 つの Sekiban のコンバーター対象の型は、`Data` の中身にかかわらずコンバーター経由で届きます。キャンセルは `OperationCanceledException` のままです。
- **クエリ。** `IQueryCommon` だけを実装し、`ResultBox` を返す静的な `HandleQuery` を持つクエリはサポートされなくなりました。実装すべきインターフェイスを示す `InvalidOperationException` で失敗します。値型を出力するリストクエリが、コア契約経由で動くようになりました。
- **MaterializedView。** protected な `MvExecutorBase.CompleteCatchUpAsync` のオーバーロードは `IEnumerable<SerializableEvent>` を受け取ります。読み取りに失敗したときの `Exception` を受け取るオーバーロードを追加しました。

変更されたシグネチャと、形以外の動作の変更は、すべて移行ガイドにまとめています。

## 依存パッケージ

- **net10.0** の依存バージョンは DCB 10.25.0 と同じです。
- **net11.0** は .NET 11.0.0 の正式版パッケージを使います。.NET と同じ版で出る `Microsoft.Extensions.*` と `Microsoft.Data.Sqlite` は `11.0.0` です。例外は PostgreSQL プロバイダーです。`Npgsql.EntityFrameworkCore.PostgreSQL` は `11.0.0-rc.3` で、Npgsql が Entity Framework Core の版を固定しているため、Entity Framework Core は `11.0.0-rc.3.26522.148` です。net11.0 の `Npgsql` は 10.0.3 です。
- `Microsoft.Orleans.*` はどちらも 10.4.2 です。Orleans 10.4.2 への更新の注意点は、10.24.0 のリリースノートを参照してください。
- `ResultBoxes` 0.4.0 に依存するのは、WithResult 系の 4 パッケージだけです。

## プレビューについて

- net11.0 の `Npgsql.EntityFrameworkCore.PostgreSQL` と、それが固定する Entity Framework Core はリリース候補版です。今後のプレビューで両方を正式版に切り替えます。すべての依存が安定版になったら、正式版 11.0.1 を出します。
- 11 系がプレビューの間も、10.x 系のリリースは続けます。

## テンプレート

11 系の 5 つの DCB テンプレートは `Sekiban.Dcb` `11.0.1-preview01`（各テンプレートの `SekibanDcbTemplateVersion.props` の `SekibanDcbVersion`）と `Microsoft.Orleans` 10.4.2 を参照し、生成されるプロジェクトは net10.0 を対象とします。テンプレートのソースは、11 の API に合わせる必要があった箇所だけを変更しています。テンプレートはこのライブラリと一緒にはリリースしません。11 のベストプラクティスに合わせた調整とリリースは、後の別ステップで行います。
