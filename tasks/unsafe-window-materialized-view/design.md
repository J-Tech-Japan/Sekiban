# Unsafe Window Materialized View 設計

**Status**: Proposal only  
**Scope**: 設計議論用。実装はこの PR では行わない。  
**Audience**: Sekiban DCB の Materialized View / MultiProjection / EventStore の設計を把握している開発者

---

## 1. 背景

現在の Postgres materialized view は、イベントを受けて SQL を適用し、テーブルへ直接反映するモデルである。

このモデルの利点:

- SQL で直接参照できる
- BI / API / 管理画面から使いやすい
- メモリ projection 全体をシリアライズせずに大規模 read model を扱える

一方で、現行設計には `Safe` / `Unsafe` の概念がない。

そのため、次のようなケースで弱い。

- stream 上でイベント到着順が前後する
- ある aggregate の `Create` より `Update` が先に見える
- DB projection が一時的に誤った順で適用される
- 後から遅延イベントが来た際に、その行だけ安全に再確定したい

MultiProjection の `SafeUnsafeProjectionState` は、一定期間を unsafe とみなし、safe promotion 時に順序を確定することでこの問題に対応している。

今回検討するのは、この考え方を **DB materialized view 側に持ち込む設計** である。

---

## 2. 設計の要点

今回の提案では、各 logical row について次の 3 層を持つ。

1. `safe table`
2. `unsafe table`
3. `merged view`

重要な前提:

- `unsafe table` は **各キー 1 行のみ**
- `unsafe` に複数履歴を持たせない
- 正しさは `unsafe` の履歴保持ではなく、**safe 化時の key 単位 replay** で担保する

つまり `unsafe` は「履歴バッファ」ではなく、**暫定最新 row のキャッシュ** である。

---

## 3. 目標

### 3.1 Goals

- 現行 materialized view に safe/unsafe の考え方を持ち込む
- 遅延イベント・順序逆転イベントに対して、最終的に正しい row を確定できる
- read 側からは常に「今見せるべき最新 row」を 1 view として参照できる
- unsafe が小さい前提で read コストを低く保つ
- AI コーディングで追従しやすいよう、テンプレート駆動の実装方式を用意する
- 自由 SQL projector を完全禁止せず、少なくとも warning と制約で unsafe モードの適合性を判定できるようにする

### 3.2 Non-goals

- 現行 `IMaterializedViewProjector` の完全互換 unsafe 化
- 任意 SQL をストレージ層が自動的に巻き戻して整合させること
- 初期段階から複数 aggregate / 複数 table をまたぐ複雑 projection を完全サポートすること
- PostgreSQL の native `MATERIALIZED VIEW` を使うこと

---

## 4. なぜ `unsafe` は各キー 1 行でよいか

ここが設計上の重要点である。

誤りやすい理解は、

- unsafe 内に同じ key の複数バージョンを保持し
- その中からどれを採用するかを query 時に選ぶ

というものだが、今回の設計ではそれを採用しない。

採用するのは次のモデル:

- `safe` は最後に確定した row
- `unsafe` はその key の「現在の暫定最新版」
- 順序が怪しい期間は `unsafe` を見せる
- safe window を超えたところで event store を key 単位に replay し、正しい順序で `safe` を再構築する

このモデルでは、`unsafe` は複数候補を持つ必要がない。

必要なのは:

- 現時点で見せる暫定 row 1 件
- safe 化するときの replay 材料

であり、replay 材料は `unsafe` ではなく **event store** が持つ。

---

## 5. 提案アーキテクチャ

## 5.1 3層構成

### Safe Table

- 各 projection key に対して 1 行
- safe window を超え、順序確定済みの row
- 通常の永続 read model の本体

### Unsafe Table

- 各 projection key に対して 1 行
- まだ順序確定していない暫定 row
- 後着イベントが来たらこの row を上書き再計算する

### Merged View

- `unsafe` に key があれば `unsafe` を採用
- なければ `safe` を採用

read 側はこの merged view だけを見ればよい。

---

## 5.2 推奨 DDL イメージ

以下は概念例であり、実際の naming はライブラリ側で統一する。

```sql
CREATE TABLE weather_forecast_safe (
    forecast_id UUID PRIMARY KEY,
    location TEXT NOT NULL,
    forecast_date DATE NOT NULL,
    temperature_c INT NOT NULL,
    summary TEXT,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,

    _projection_key TEXT NOT NULL,
    _last_sortable_unique_id TEXT NOT NULL,
    _last_event_version BIGINT NOT NULL,
    _last_applied_at TIMESTAMPTZ NOT NULL,
    _safe_confirmed_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE weather_forecast_unsafe (
    forecast_id UUID PRIMARY KEY,
    location TEXT NOT NULL,
    forecast_date DATE NOT NULL,
    temperature_c INT NOT NULL,
    summary TEXT,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,

    _projection_key TEXT NOT NULL,
    _last_sortable_unique_id TEXT NOT NULL,
    _last_event_version BIGINT NOT NULL,
    _last_applied_at TIMESTAMPTZ NOT NULL,
    _unsafe_since TIMESTAMPTZ NOT NULL,
    _safe_due_at TIMESTAMPTZ NOT NULL,
    _needs_rebuild BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE VIEW weather_forecast_current AS
SELECT *
FROM weather_forecast_unsafe
UNION ALL
SELECT s.*
FROM weather_forecast_safe s
WHERE NOT EXISTS (
    SELECT 1
    FROM weather_forecast_unsafe u
    WHERE u._projection_key = s._projection_key
);
```

`UNION ALL + NOT EXISTS` を使うか、`LEFT JOIN` で unsafe 優先にするかは DB に応じて選べる。

---

## 6. この設計が成立する条件

この設計は、現行の「自由 SQL projector」をそのまま完全に受け入れるわけではない。

成立条件は次の通り。

### 6.1 Key が明確であること

projection row に対して、再計算単位となる key が明示できる必要がある。

例:

- `forecast_id`
- `customer_id`
- `order_id`

### 6.2 Key 単位で replay できること

safe 化の際に、

- その key の safe row を起点に
- その key に関係するイベントだけを
- `SortableUniqueId` 順に取得し
- row を再構成する

必要がある。

したがって event store 側にも、少なくとも logical key / aggregate key / projection key ベースの read 経路が必要になる。

Sekiban DCB では event が複数 tag を持ち得るため、unsafe-window MV の contract には

- `projection key -> どの tag 群を読むか`

の対応も必要になる。

少なくとも framework からは次の情報が見える必要がある。

- `IEnumerable<ITag> TagsForProjectionKey(string projectionKey)`
- `EventStore.ReadEventsForTagsAsync(tags, sinceSortableUniqueId)` 相当の API

また replay は毎回先頭から行うのではなく、

- `safe row` の `_last_sortable_unique_id`
- `safe row` の `_last_event_version`

を起点に、その続きだけを incremental replay できる必要がある。

つまり `SafeRowRebuildContext` は概念的に、

- `StartingRow`
- `EventsSince(startingRow.LastSortableUniqueId)`

を持つ形に寄せるのがよい。

### 6.3 1 key = 1 current row に近いこと

v1 の unsafe-window MV は、次のような projector に向く。

- 1 key -> 1 row
- row state が key 単位 replay で閉じる
- 1 event から複数 key へ fan-out しても、各 key が独立に replay できる

したがって v1 でも、`ClassRoomEnrollmentMvV1` のように

- 1 event が複数 logical key を更新する
- ただし各 key ごとの current row は独立に再構築できる

モデルまでは射程に入れてよい。

逆に難しいもの:

- 複数 aggregate を join して 1 row を形成する
- 1 key に対して複数 table を同時に強整合更新したい
- 1 event が複数 key に波及するが、各 key ごとの replay 境界が切れない

これらは将来拡張としてはあり得るが、unsafe-window v1 の対象から外した方が良い。

---

## 7. 現行 Materialized View との関係

今回の提案は、現行 `MaterializedView` に「透過的に後付けする」よりも、**別モードとして追加する** 方が適切である。

ただし、分離対象は主に **authoring contract** であり、runtime 全体を完全に分けることまでは意味しない。

共有候補:

- registry
- grain / actor
- catch-up / promotion worker 基盤
- diagnostics / monitoring

つまり「別モード = 別 package / 別 grain / 別 runtime」とは限らず、利用者が実装する projector contract と correctness 制約を分ける、という整理が適切である。

### 推奨分離

- **Current MV mode**
  - 現行通り
  - 自由 SQL
  - 単一 table / 複数 table を問わない
  - safe/unsafe なし

- **Unsafe Window MV mode**
  - key-aware
  - safe/unsafe/current の 3 層
  - replay by key 必須
  - row metadata 必須

この分離により、既存利用者に不必要な制約を課さずに新モードを追加できる。

---

## 8. 新しい authoring contract の方向性

完全な API 名は今後詰めるが、現時点では「現行 `IMaterializedViewProjector` の拡張」よりも「新しい keyed row projector 契約」を切る方がよい。

仮称:

- `IUnsafeWindowMaterializedViewProjector<TRow>`
- `IKeyedRowMaterializedViewProjector<TRow>`

少なくとも次を表現できる必要がある。

### 8.1 必須情報

- `ViewName`
- `ViewVersion`
- `SafeWindow`
- `ProjectionKey` の定義
- event から 0..n 個の `ProjectionKey` を導出できること
- `ProjectionKey -> tag(s)` の対応
- row の primary key
- replay 可能性 (`CanReplayByKey`)

### 8.2 必須メソッド案

```csharp
public interface IUnsafeWindowMaterializedViewProjector<TRow>
{
    string ViewName { get; }
    int ViewVersion { get; }
    TimeSpan SafeWindow { get; }

    Task InitializeAsync(IUnsafeWindowMvInitContext ctx, CancellationToken ct);

    IEnumerable<string> GetProjectionKeys(IEvent ev);

    IEnumerable<ITag> TagsForProjectionKey(string projectionKey);

    Task<TRow?> BuildUnsafeRowAsync(
        UnsafeRowBuildContext<TRow> ctx,
        CancellationToken ct);

    Task<TRow?> RebuildSafeRowAsync(
        SafeRowRebuildContext<TRow> ctx,
        CancellationToken ct);
}
```

意図:

- `BuildUnsafeRowAsync`
  - stream 受信時に暫定 row を作る
  - 最速表示のための path
- `RebuildSafeRowAsync`
  - safe 化時に event store replay を使って正しい row を作る
  - correctness path

この 2 経路を明示的に分けると、unsafe 更新と safe promotion の責任分界が明確になる。

一方で、review で指摘された通り `BuildUnsafeRowAsync` と `RebuildSafeRowAsync` にロジック drift が起きやすい懸念もある。

そのため contract の本命候補としては、次のような **単一 Apply primitive** も有力である。

```csharp
public interface IUnsafeWindowMaterializedViewProjector<TRow>
{
    string ViewName { get; }
    int ViewVersion { get; }
    TimeSpan SafeWindow { get; }

    IEnumerable<string> GetProjectionKeys(IEvent ev);

    IEnumerable<ITag> TagsForProjectionKey(string projectionKey);

    Task<TRow?> ApplyAsync(
        TRow? current,
        IEvent ev,
        UnsafeWindowApplyContext ctx,
        CancellationToken ct);
}
```

この場合は、

- fast path: `unsafe row` に対して `ApplyAsync` を 1 回適用
- rebuild path: `safe row` を起点に event 列を fold

となり、MultiProjection の `Project` に近い mental model に揃えられる。

現時点では split / unified の最終判断は open question としつつも、**runtime は unified primitive を内部表現に寄せられるよう設計すべき** である。

---

## 9. 必須メタデータ列

unsafe-window MV のテーブルには、最低限次の列を必須にするのがよい。

### 共通

- `_projection_key`
- `_last_sortable_unique_id`
- `_last_event_version`
- `_last_applied_at`
- `_is_deleted`

### safe 専用

- `_safe_confirmed_at`

### unsafe 専用

- `_unsafe_since`
- `_safe_due_at`
- `_needs_rebuild`

これらがない場合は:

- unsafe-window モードでは startup 時の schema validation を行う
- 必須列欠如 / 型不整合は fail-fast にする
- `_projection_key` が null / empty を返す projector は登録拒否にする

warning は補助としては有効だが、correctness は warning ではなく runtime guarantee で守るべきである。

---

## 10. 書き込みフロー

## 10.1 Stream 受信時

1. イベントを受信
2. projector が `projection key` を決定
3. 対応する `unsafe` row を取得
4. Fast path 判定

### Fast path

条件:

- unsafe row が存在しない、または
- incoming `SortableUniqueId` が unsafe row より新しい

処理:

- `BuildUnsafeRowAsync` で暫定 row を生成
- `unsafe` を upsert
- `_safe_due_at = event.occurred_at + SafeWindow`

### Repair path

条件:

- incoming `SortableUniqueId` が unsafe row より古い
- または順序逆転が検知された

処理:

- `_needs_rebuild = true` を **必ず** 立てる
- 可能ならその場で key replay により unsafe row を再構成
- 少なくとも safe promotion 前には必ず rebuild

重要なのは、`incoming SUID < unsafe SUID` を「古いから無視」で終わらせないことである。

例えば `Update(v=2)` が先に届いて unsafe row を作った後に `Create(v=1)` が遅延到着した場合、古いイベントを skip すると create 時の default 値や existence 条件を失ったまま safe promotion まで進んでしまう。

したがって **古い SUID の到着自体が repair シグナル** であり、即時 rebuild しない実装を選ぶ場合でも `_needs_rebuild` を立てることは規約にする必要がある。

ポイント:

- `unsafe` が 1 行しかない以上、順序逆転を完全に unsafe 自身だけで吸収することはできない
- その代わり、順序逆転時には replay path に逃がす

---

## 10.2 Safe Promotion

バックグラウンド worker が `unsafe` から promotion 対象を拾う。

候補条件:

- `_safe_due_at <= now()`

worker は次のように動く。

1. promotion 対象 key を lock 付きで取得
2. その key の `safe` row を読む
3. event store から key に関係するイベントを、`safe row._last_sortable_unique_id` 以降だけ正順で取得
4. `RebuildSafeRowAsync` で正しい row を再構成
5. `safe` に upsert
6. 対応する `unsafe` row を delete

Postgres では `FOR UPDATE SKIP LOCKED` を使う設計が自然。

さらに correctness のためには次も必要である。

- `read safe -> read events -> rebuild -> upsert safe -> delete unsafe` を 1 transaction に収める
- promotion 中状態を検出するための `_in_promotion_at` などを持ち、crashed worker の stale lock を回収できるようにする
- promotion 完了後に遅延 stream event が到着したときは、`incoming.SUID <= safe._last_sortable_unique_id` なら skip、より新しければ unsafe に再投入する

これにより promotion worker の冪等性と crash 耐性を確保できる。

---

## 11. Query 側の見え方

read 側は merged view だけを見る。

期待される振る舞い:

- 通常時: safe row が見える
- 直近更新後: unsafe row が優先して見える
- safe promotion 後: safe row に戻る

このため、クライアントや API は safe/unsafe の存在を意識せずに `current` view を読める。

必要なら管理画面・診断 API からだけ safe/unsafe の両方を見られるようにする。

Delete の扱いは v1 で明示しておく必要がある。

- internal な current view では tombstone row (`_is_deleted = true`) を保持する
- consumer 向け API では `WHERE NOT _is_deleted` を掛けた公開 view を別に持つこともできる
- delete 後の recreate、tombstone retention は contract と運用ポリシーの両面で決める必要がある

---

## 12. パフォーマンス評価

## 12.1 Read

良い。

理由:

- 参照主体は safe table
- unsafe は「最近更新の key」だけ
- unsafe 数が少なければ merged view のオーバーヘッドは小さい

必要な index:

- `safe(_projection_key)`
- `unsafe(_projection_key)`
- `unsafe(_safe_due_at)`
- 必要に応じて business key index

unsafe 急増時には merged view の plan が崩れ得るため、次の備えも必要である。

- `unsafe_count`
- `oldest_unsafe_age`
- `promotion_lag`

を必須メトリクスとして露出する。

加えて将来的には、

- `unsafe_count > threshold` 時に safe only を返す degraded mode
- `unsafe WHERE _needs_rebuild = false` の partial index

も検討対象になる。

## 12.2 Write

現行 MV より重くなる。

主なコスト:

- unsafe upsert
- 順序逆転時の rebuild
- safe promotion worker

ただし、unsafe table が 1 key 1 row であるため、write amplification は比較的抑えられる。

## 12.3 Replay / Promotion

ここが correctness の対価。

負荷は次に依存する。

- safe window の長さ
- key ごとのイベント密度
- event store の key-based read 性能

1 分, 2 分, 10 分のように bounded され、かつ key ごとのイベント数が常識的なら十分現実的。

---

## 13. 最大の設計制約

最大の制約は、**現行の自由 SQL モデルではストレージ層が自動的に key/replay 単位を理解できない** 点である。

そのため、unsafe-window MV は次の方針が必要。

- key / replay / metadata の contract を framework 側で強制する
- runtime schema validation を fail-fast で行う
- テンプレートはサンプルとして提供する
- 必須メタ列を持たせる
- key-aware projector contract を明示する

これは「自由度を少し落とす代わりに correctness を得る」設計であり、妥当な trade-off である。

---

## 14. テンプレート戦略

利用者が AI コーディングで実装しやすいよう、unsafe-window MV はテンプレートを標準で持つべきである。

ただしテンプレートは補助であり、correctness の本体は framework 側の生成・検証に置くべきである。

少なくとも次を提供したい。

### 14.1 テンプレートに含めるもの

- `safe table` DDL
- `unsafe table` DDL
- `current view` DDL
- 必須 metadata 列
- promotion worker 雛形
- projector 実装サンプル
- key-based replay の例

### 14.2 warning / validation

startup 時を主として、次をチェックする。

- 必須列があるか
- `_projection_key` と PK の対応が取れているか
- `RebuildSafeRowAsync` または unified `ApplyAsync` が実装されているか
- replay by key が可能と宣言されているか
- `_projection_key` が null / empty を返さないか

さらに有力案として、

- `[MvColumn]` 付き POCO から `safe table` / `unsafe table` / `current view` を framework が生成する

方式を検討する。

warning は補助だが、必須要件違反は fail-fast にすべきである。

---

## 15. 実装順序の提案

### Phase 1: 設計と contract 固定

- key-aware projector contract を決める
- 必須 metadata 列を決める
- key-based replay の event store API 要件を決める
- fixed safe window と dynamic extra safe window の役割分担を決める

### Phase 2: 1 row / 1 key の最小 PoC

- Postgres 限定
- 1 table
- 1 key = 1 row
- safe / unsafe / current を実装
- promotion worker を実装

### Phase 3: 管理 API / UI

- safe row count
- unsafe row count
- oldest unsafe age
- promotion lag
- rebuild count
- degraded mode 判定材料

### Phase 4: 複数 key fan-out の検証

- 1 event -> 複数 projection key の projector を試す
- key ごとの replay 境界が守れる contract を詰める

### Phase 5: 複数 table / 複数 key の拡張判断

- ここで初めて高度な projector に広げる

---

## 16. 結論

この設計は実現可能であり、方向性として妥当である。

ただし、成功条件は明確である。

- `unsafe` は各 key 1 行
- 正しさは `safe` 昇格時 replay で担保する
- そのために key-aware contract が必要
- 現行 MV にそのまま後付けするのではなく、unsafe-window 対応モードとして分離する

この分離を受け入れるなら、

- read 性能は良い
- correctness は高められる
- 遅延イベント耐性を DB projection に持ち込める

次に詰めるべき論点は open questions に整理する。
