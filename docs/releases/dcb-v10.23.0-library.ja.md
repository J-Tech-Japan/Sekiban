# Sekiban DCB 10.23.0

- バージョン: `10.23.0`。DCB ライブラリは 26 パッケージです。net9.0 と net10.0 を対象とし、`Microsoft.Orleans.*` は 10.3.1 のままです。
- アップグレードにデータやスキーマの移行は必要ありません。10.23.0 を参照して再コンパイルしてください。シグネチャの変更 1 件と注意が必要な動作については「アップグレード前の確認」をご覧ください。

## 修正

- **Multi-projection の safe state と入力を変更する projector**（issue #1253）。入力を変更してそのまま返す projector は safe state を破損させる可能性がありました。修正の適用は payload ごとのオプトインです。payload に `IMutatesProjectionInput` を実装してください。「アップグレード前の確認」をご覧ください。
- **プロセス間のクロックずれによる更新の消失。** コマンドが書き込む整合性タグについて、観測済みの予約位置より大きなイベント ID を採番するようになりました。適用範囲は「アップグレード前の確認」をご覧ください。
- **確定後に古いまま残るタグバージョンのキャッシュ**（issue #1276）。予約の確定時に予約ロック内でキャッシュを無効化し、次のアクセスで同じロック内からストアを読み直します。
- **Cosmos DB の最新タグ検索**では、空の結果ページでタグなしと判断せず、次のページの検索を続けるようになりました。
- **Orleans の `MultiProjectionGrain` がすべての actor オプションを引き渡すようになりました。**
- **tombstone 状態のチェックポイントの再構築**では、force-full catch-up が途中まで進んだ後は増分で再開し、catch-up 完了時に永続化します。また、ストリーミング永続化経路で永続的な再構築マーカーを消去し、再構築マーカーが残っている場合もチェックポイントスロットを引き継ぎます。
- **設定された Blob プレフィックスがスナップショット Blob にも適用されるようになりました**（Azure Blob Storage と S3）。「アップグレード前の確認」をご覧ください。
- activation 時に、既知の projector バージョン変更によってスナップショットが見つからない場合、再構築を情報レベルで記録します。同じバージョンの場合や以前のバージョンが不明な場合は、引き続き警告レベルで記録します。

## 設定可能な機能とオプトイン機能（従来の既定値を維持）

- **hot のみの catch-up におけるチェックポイント保存間隔**を設定できるようになりました。`HotCatchUpPersistMaxFetchedEvents` の既定値は 5000、`HotCatchUpPersistMaxIntervalSeconds` の既定値は 300 です。
- **catch-up 中の初回クエリの待機上限**: `FirstQueryCatchUpMaxWaitMs` は既定値 0 で無効です。有効にした場合、呼び出し側は処理待ちの結果に対応する必要があります。上限に達すると state と snapshot の呼び出しはエラー結果を返し、クエリは catch-up 中を示すプレフィックス付きの `InvalidOperationException` を送出します。後で再試行してください。この上限には activation とリクエストのキュー待ちは含まれません。また、成功したクエリにも通常の projection の遅延があり、最新性は保証されません。参照: `docs/dcb_llm/13_common_issues.md` の "First query times out during catch-up (SEK-G90; #1253 item 5)"。
- **`OffloadKeyEnumerator`** は、渡された multi-projection state store の現在の ServiceId で参照されているスナップショット Blob のキーを列挙します。`Undecodable` の項目を返すことがあり、1 回の列挙で見つからなかっただけでは削除してよいとは判断できません。参照: `docs/dcb_llm/04_multiple_aggregate_projector.md` の "Maintenance-window snapshot blob pruning (#1253 item 3)"。
- **予約から導出する PostgreSQL のタグヘッドフェンス**: `TagConsistencyFenceMode.DeriveFromReservations`（既定値は `Off`）。コマンドが読み取った（または明示的な位置を与えられた）整合性タグを、書き込むイベントにも付ける場合、そのタグへの古い位置に基づく書き込みをストレージ層で拒否します。例えば、同じタグの grain に 2 つの activation が存在する場合に適用されます。読み取らずに書き込むタグと整合性タグ以外は対象外で、読み取り集合全体の保証ではありません。有効化には準備が必要です。モードを無効にしたまま、フェンスを迂回する writer を停止して処理完了を待ち、サービスの enablement epoch を用意してから有効にしてください。epoch がない場合、コマンドは handler の実行前に失敗します。Orleans executor の型付きコマンドでは `IConditionalCommandExecutor` を介してコマンドごとにモードを選べます。シリアライズ済み commit はグローバル設定を使います。参照: `docs/dcb_llm/13_common_issues.md` の "Choosing between fast and strict tag consistency"。

## アップグレード前の確認

- **`MultiProjectionGrainStatus` のシグネチャ。** この位置引数を持つ record に、既定値付きの 3 メンバー（`FirstQueryCatchUpPending`、`CatchUpTargetPosition`、`LastBackgroundCatchUpError`）が追加されました。プロパティの読み取りやインスタンスの構築を行うコードは再コンパイルだけで対応できます。位置による分解を行うコードは変更が必要です。10.22.0 を参照してビルドされ、この record を構築または分解するアセンブリは再ビルドしてください。
- **入力を変更する projector。** アップグレードだけでは保護されません。payload に `IMutatesProjectionInput` を実装し、`GenerateInitialPayload` が呼び出しごとに新しいインスタンスを返すようにしてください。dual-state wrapper を直接構築する場合は `DcbDomainTypes` を受け取るオーバーロードを使ってください（従来のオーバーロードはマーカー付き payload に対して例外を送出します）。wrapper の構築時に初期のマーカー付き payload を複製します。スナップショット復元時には独立した safe と unsafe のインスタンスを作ります（ストリーミング経路では 2 回デシリアライズし、マーカー付きの両者が同じインスタンスなら複製します）。すべての再調整で safe を複製して unsafe を作ります。バッファが空の場合や、catch-up・再構築中の順序どおりの safe イベントごとの再調整も含みます。遅延修復の失敗をロールバックする際も、保存していた safe と unsafe が同じインスタンスなら複製します。複製時間は state の大きさに比例します。順序どおりの unsafe の処理では独立済みのインスタンスを再利用し、追加の複製は行いません。マーカーのない projector は、診断を有効にしない限り従来の動作とコストを維持します。safe state を変更する未宣言の projector を見つけるには、`VerifySafeStateIsolation`（既定では無効）を一時的に有効にしてください。unsafe の処理の前後、再調整の前、served state へのイベント再適用の後に safe state のシリアライズ結果のバイト列を比較し、変化があれば `InvalidOperationException` を送出します。この診断にはシリアライズのコストがかかり、非決定的なシリアライズでは誤検知が起こり得ます。参照: `docs/dcb_llm/04_multiple_aggregate_projector.md` の "Dual-State Convergence & Safe-Window Graduation (SEK-G18)"。
- **クロックずれ。** イベント ID は論理時刻であり、実際の時計より先に進むことがあります。その間、multi-projection は該当イベントを unsafe window に保持し、cold event のエクスポートは時計が追いつくまで待ちます。保存済みの遠い未来の位置を観測した場合、再起動しない限り、時計を修正してもその位置は generator の下限として残ります。予約に基づく修正は書き込む整合性タグの観測済み位置が対象で、読み取りのみのタグ、読み取らずに書き込むタグ、整合性タグ以外は対象外です。ヘッドの直接読み取りは追跡されません。すでに順序が逆転して保存されたイベントは修復しません。参照: `docs/dcb_llm/13_common_issues.md` の "Clock skew between processes (SEK-G116)"。
- **Blob プレフィックス。** プレフィックスを設定している場合、新しいスナップショット Blob は `{prefix}/...` の下に保存されます。以前の Blob は古いキーのまま引き続き読み取れるため、移行は不要です。同じ内容もプレフィックスの下にもう一度保存されます。プレフィックス付きの内容に基づくキーを使う seekable な書き込みでは、キーが 512 文字を超えるとアップロード前に拒否されます。PostgreSQL が許容する名前とバージョンの長さでは、プレフィックスが 57 文字以下ならこの制限に収まります。プロバイダーの命名規則も適用されます。プレフィックスがなければ変更はありません。
- **Materialized view。** 組み込みの registry store（PostgreSQL、SQL Server、MySQL、SQLite）では、バッチ適用時に registry 行をロックするようになりました。stream 以外の適用では期待する位置も再確認します。このロックは常に有効です。独自の `IMvRegistryStore` は、新しいメンバーを実装しない限り従来の動作を維持します。`MvCatchUpOutcome` に `Superseded` が追加されました。すべての値を扱う switch には分岐を追加してください。
- **独自のストア実装。** このリリースでインターフェースに追加されたメンバーには既定の実装があります。

## ドキュメント

- grain の重複 activation、Orleans の grain directory の選択肢、fast と strict の整合性の選択。
- 永続的に tombstone 状態になったチェックポイントの検出と復旧。
- メンテナンス時間帯でのスナップショット Blob の削除と、ローカルのスナップショット読み取りキャッシュ。
- catch-up 中の turn の長さに関する警告。
