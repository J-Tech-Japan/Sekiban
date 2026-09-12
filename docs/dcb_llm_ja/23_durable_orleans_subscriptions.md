# 永続 Orleans サブスクリプション

`Sekiban.Dcb.Postgres` は、Orleans プロセスの再起動後もカーソルを保持したいアプリケーション向けに、
オプトインの PostgreSQL 永続サブスクライバーを提供します。Orleans ストリームはデータを渡す通知ではなく、
再読込を促すヒントです。正しいイベント列は PostgreSQL のイベントストアから取得し、永続化された確認済み
位置より後のイベントだけを Durable runner が読み取ります。

## 登録

通常の PostgreSQL イベントストアを登録してから、サービス ID と名前の組み合わせごとに登録します。

```csharp
builder.Services.AddSekibanDcbPostgres(connectionString);
builder.Services.AddSekibanDcbPostgresDurableSubscription(
    options =>
    {
        options.ServiceId = "orders";
        options.Name = "billing";
        options.StartPolicy = DurableSubscriptionStartPolicy.FromBeginning;
        options.ProvisioningMode = DurableSubscriptionProvisioningMode.PreProvisioned;
    },
    async (eventRecord, cancellationToken) =>
    {
        await HandleEventAsync(eventRecord, cancellationToken);
    });
```

既定値の `FromNow` は現在の安全な末尾から開始し、`FromBeginning` は null カーソルから開始します。同一
サービスコンテナー内の名前は一意であり、重複登録は hosted runner が追加される前に拒否されます。既存の
`IEventSubscription` コールバック経路は変更されず、Durable subscription へ暗黙変換されません。

## PostgreSQL のデプロイ

Pre-provisioned モードを使う前に、所有者が raw table を作成します。

```csharp
await serviceProvider.ProvisionDurableSubscriptionSchemaAsync(cancellationToken);
```

`PreProvisioned` の実行時処理は DML のみです。永続行にはサービス/名前、初期化状態と確認済みカーソル、
フェーズ、失敗と停止の証跡、所有者/generation、lease の期限、再試行時刻が保存されます。テーブルまたは列が
存在しない場合は PostgreSQL のプロバイダーエラー（例: テーブル欠落は SQLSTATE `42P01`、列欠落は `42703`）
として通知され、存在しない状態行を正常扱いしません。

既定の `LegacyAutoProvision` は起動時に provisioning を許可するアプリケーションとの互換性を保ちます。
実行用 DB principal に DDL を許可しない場合は、所有者による provisioning を一度実行してください。

## 復旧とフェンシング

各 catch-up サイクルは PostgreSQL の lease 取得前に永続行を読み取ります。取得、更新、確認、失敗記録、resume、
halt はサービス/名前/owner/generation の組み合わせで保護されます。ハンドラーの成功はイベント変換と処理が
完了した後にだけ確認されます。変換エラーは直ちに halt し、ハンドラーエラーは既定で `MaxHandlerAttempts` 回
（4 回）まで再試行し、その後に失敗位置/理由を保存して halt します。`ResumeAsync` は明示的な操作であり、
確認済みカーソルと停止証跡を保持し、再試行/lease をクリアして owner generation を進めます。

長いハンドラーの実行中は lease を更新し、更新によってフェンスを失った場合はハンドラーをキャンセルします。
重複または順序が前後した Orleans ヒントがカーソルを進めることはなく、プロセス再起動後は PostgreSQL の行から
再開します。これは外部副作用に対する exactly-once を保証する境界ではないため、ハンドラーは冪等にしてください。

lease の所有、期限、再試行可能時刻、halt のフェンシングはクライアント時計ではなく PostgreSQL の時刻で判定します。
永続化される `DurableSubscriptionPhase` と、handle から見えるプロセスローカルな
`DurableSubscriptionRunnerStatus`（`Starting`、`Owner`、`Standby`、`Halted`、`Stopped`）は分離されています。
同じ owner からの nudge や fallback poll は lease と generation を保持し、empty/idle の観測でも lease を解放しません。
そのため lease の期限切れや新しい generation を待たずに速やかに catch-up できます。再試行は、再起動や takeover の後も、
永続化された `next_attempt_at_utc` が PostgreSQL 上で期限に達した場合だけ許可されます。古い owner は最初に記録された halt の
位置、理由、時刻を上書きできません。`PreProvisioned` と `LegacyAutoProvision` を混在させた登録は登録順にかかわらず
fail-closed となり、provider 登録が一部だけ残って provisioning mode が暗黙に変わることもありません。

Orleans 統合は DCB がサポートする `Microsoft.Orleans.*` **10.3.1** 系列に従います。クラスタ全体を同時に更新し、
Orleans バージョンを混在させないでください。これは durable store を正とする catch-up 境界だけを提供します。
relay/outbox、任意の外部 subscriber 副作用の replay、または自動的な過去イベント修復は提供しません。これらは
後続の別設計の対象です。

## 制限と状態確認

`SafeWindow`、`LeaseDuration`、`PollInterval`、`RetryDelay`、`MaxHandlerAttempts`、`MaxBatchSize` は hosted runner
起動前に検証されます。`IDurableSubscriptionHandle` の `GetStateAsync`、`ResumeAsync`、`HaltAsync` で永続化された
フェーズと停止証跡を確認できます。Orleans の通知は authoritative なイベント payload をハンドラーへ渡しません。
