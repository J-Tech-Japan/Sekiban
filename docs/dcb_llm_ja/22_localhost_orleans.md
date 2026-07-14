# localhost Orleans と、インメモリスタックの居場所

## 分類(taxonomy)

環境は 3 つあり、これは連続的なスライダーではありません。それぞれに正解が 1 つずつあります。

| | エグゼキューター | イベントストア | 入手方法 |
|---|---|---|---|
| **実環境**(本番、および実質本番のステージング) | 分散ランタイム (Orleans) | 永続 (Postgres / Cosmos / DynamoDB / Sqlite ファイル) | クラスタ + `AddSekibanDcbProductionGuard()` |
| **ローカル開発** | 分散ランタイム (Orleans、単一サイロ、自分のマシン上) | **明示的に選ぶ** — 永続でも揮発でも可 | `silo.UseSekibanDcbLocalhost()` |
| **ユニットテスト** | インプロセス (`InMemoryDcbExecutorForTesting`) | 揮発 (`InMemoryEventStore`) | `Sekiban.Dcb.*.Testing` パッケージ |

**欠けていたのは真ん中の行です。** そしてそれが欠けていたからこそ、一番下の行が一番上の行に漏れ出しました。ローカルで手軽に動かせるものがインメモリエグゼキューターしかなければ、人はインメモリエグゼキューターを動かします。そして誰かがそれを本番ホストに登録します — コマンドはすべて成功し、イベントは 1 件もデータベースに届かず、何も警告しません。**それは実際に起きました。** この一連の作業はすべてそれが理由です。

そこで、ローカル開発にも**本物の Orleans ランタイム**を、安価に用意します。

## 構成

```csharp
builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
```

単一サイロ、`UseLocalhostClustering`、インメモリのグレインストレージとストリーム、外部クラスタリング依存なし、インストール不要。これは**本物の** Orleans ランタイムです。グレインは配置され、ペイロードはシリアライズされ、プロジェクションは本番と同じコードパスを通ります。自己申告も `DistributedRuntime` です — 実際にそうだからです。

一方で、**イベントストアは意図的に選びません**。その 1 行はあなたのものです。

```csharp
// 現実的なローカル環境: 本物のランタイム + 本物のデータベース
builder.Services.AddSekibanDcbPostgres(builder.Configuration);

// 停止すればすべて忘れる高速な環境。正当な選択です — ただし明示的に。そして Production では決して。
builder.Services.AddSingleton<IEventStore>(new InMemoryEventStore(domainTypes.EventTypes));
```

どちらも正当です。どちらも**暗黙であってはなりません**。`AddSekibanDcbStartupBanner()` が起動のたびに、実際にどちらを掴んだかを教えます。

### Web

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
builder.Services.AddSingleton(DomainType.GetDomainTypes());
builder.Services.AddSekibanDcbPostgres(builder.Configuration);
builder.Services.AddSingleton<ISekibanExecutor>(sp => new OrleansDcbExecutor(
    sp.GetRequiredService<IClusterClient>(),
    sp.GetRequiredService<IEventStore>(),
    sp.GetRequiredService<DcbDomainTypes>()));
builder.Services.AddSekibanDcbStartupBanner();

var app = builder.Build();
app.MapPost("/students", async (ISekibanExecutor executor, CreateStudent command) =>
    await executor.ExecuteAsync(command));
app.Run();
```

サイロは Web ホストとともに起動し、ともに停止します。Sekiban のテンプレートが既に行っている構成です。

### Worker

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
// ...ドメイン / ストア / エグゼキューターの登録は同じ...
builder.Services.AddHostedService<ProjectionWorker>();
builder.Build().Run();
```

`BackgroundService` はサイロ起動後に開始し、他のサービスと同様に `ISekibanExecutor` を解決できます。シャットダウン時はホストがまずワーカーを止め、その後サイロをドレインするため、処理中の作業は放棄されずに完了します。

### CLI / バッチ — 起動して、1つ処理して、終了する

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
// ...ドメイン / ストア / エグゼキューターの登録は同じ...

using var host = builder.Build();
await host.StartAsync();                       // これが返った時点でサイロは起動済み

var executor = host.Services.GetRequiredService<ISekibanExecutor>();
await executor.ExecuteAsync(new CreateStudent(id, name, 3));

await host.StopAsync();                        // graceful drain: 返った時点でサイロは停止済み
```

`StartAsync` / `StopAsync` は決定的です。`StopAsync` が返った時点でサイロは実際に停止しているため、バッチ処理は「書き込みが終わったことを祈る」のではなく「終わったと知って」終了できます。

**引き換えに支払うコストを、はっきり書きます。** サイロにはコールドスタートがあります。開発マシンでおよそ 1 秒、しかも短命プロセスでは**起動のたびに**支払います。インメモリエグゼキューターで即座に立ち上がっていた CLI は、もう即座には感じられません。これは実在のコストであり、**CLI が本番と同じランタイムを通る**ことの対価です。1 秒が問題になるほどタイトなループで CLI を回すなら、起動済みの長寿命ローカルホストに対して実行してください(呼び出しごとに新しいサイロを立ち上げない)。

3 つの形すべてが `dcb/tests/Sekiban.Dcb.Orleans.Tests/LocalhostCompositionTests.cs` でテストされています — **実際のホストを、実際に起動し、実際に使い、実際に停止**しています。どれかが動かなくなれば、このファイルが落ちます。

## Testing パッケージ

揮発スタックは、ランタイムプロジェクトが参照する理由のないパッケージに移りました。

| パッケージ | 内容 |
|---|---|
| `Sekiban.Dcb.Core.Testing` | `InMemoryEventStore`、`InMemoryMultiProjectionStateStore`、`InMemoryObjectAccessor`、インプロセスの publisher/stream 群、`InMemoryBlobStorageSnapshotAccessor` — 名前空間 `Sekiban.Dcb.Testing` |
| `Sekiban.Dcb.WithResult.Testing` | `ResultBox` ファサード用の `InMemoryDcbExecutorForTesting` |
| `Sekiban.Dcb.WithoutResult.Testing` | 例外ベースファサード用の `InMemoryDcbExecutorForTesting` |

```csharp
using Sekiban.Dcb.Testing;

var eventStore = new InMemoryEventStore(domainTypes.EventTypes);
var executor = new InMemoryDcbExecutorForTesting(domainTypes, eventStore);
```

2 つのファサードを**別パッケージ**にしたのは意図的です。両方のエグゼキューターを 1 つの "Sekiban.Dcb.Testing" に入れると、どちらを import したかで意味が変わるパッケージになってしまいます。

**境界が何であるかを正確に言います。** `Sekiban.Dcb.Testing` の入口(`InMemoryDcbExecutorForTesting` とその周りのストア群)には**これらのパッケージへの参照が必要**です。参照していないプロジェクトは、これらの型に到達できません。これは**新しい扉**の周りに立つ、本物の境界です。ランタイムプロジェクトがテスト用エグゼキューターを「うっかり手に取る」ことはできなくなりました。

**ただし、古い扉の周りの壁ではありません。** `Sekiban.Dcb.InMemory.InMemoryDcbExecutor` と旧ストア群は互換性のためランタイムパッケージ内に public のまま残り、既存コードは**これまでどおりコンパイルでき、動作します**。それが互換性の約束であり、意図的なものです。この経路に対する防御は `[Obsolete]` による誘導と、`AddSekibanDcbProductionGuard()` です。ガードは、**どの名前で入手したかに関わらず**、Production ホストがインプロセスエグゼキューターや揮発ストアを解決していれば起動を拒否します。旧入口が閉じるのは次のメジャーです。それまで前に立ちはだかるのはコンパイラではなく、**ガード**です。

## 移行

**既存コードは壊れません。** 旧型はこれまでどおり動作し、挙動も同一で、削除ではなく `[Obsolete]` です。次のメジャーバージョンより前に削除されることはありません。

使用箇所を探す:

```bash
grep -rn "InMemoryDcbExecutor\|Sekiban\.Dcb\.InMemory" --include=*.cs .
```

形ごとの対応:

| 現状 | 意味 | 対応 |
|---|---|---|
| テスト内の `new InMemoryDcbExecutor(domainTypes)` | 目に見えないプライベートな揮発ストア | `new InMemoryDcbExecutorForTesting(domainTypes, new InMemoryEventStore(domainTypes.EventTypes))` |
| テスト内の `new InMemoryDcbExecutor(domainTypes, 揮発ストア)` | 正しく構成されたユニットテスト | 型名を変更し、`Sekiban.Dcb.*.Testing` を参照、`using Sekiban.Dcb.InMemory` を `using Sekiban.Dcb.Testing` に |
| **実際に動くものの中の `new InMemoryDcbExecutor(domainTypes, 永続ストア)`** | **これが危険なケース** — 永続ストアなのでイベントは残りますが、コマンドは依然としてインプロセスのアクターで実行され、クラスタ協調がありません。**複数ホストは互いのタグ予約を見られません** | ローカルなら**localhost サイロ**(本ドキュメント)へ、そうでなければ実クラスタへ移行してください。実環境で安全なインプロセスエグゼキューターは存在しません。だからこそ `AddSekibanDcbProductionGuard()` はこれに対して fail-closed します |
| テスト内の `Sekiban.Dcb.InMemory.InMemoryEventStore` など | 正しく使われた揮発ストア | `Sekiban.Dcb.Core.Testing` を参照し、`using` を変更 |
| `InMemoryTagStatePersistent` | **テストダブルではありません** — タグ状態アクターの実プロセス内キャッシュ | そのままにしてください。`Sekiban.Dcb.Core` に残り、`[Obsolete]` でもありません |

**このリポジトリを先に移行しました。** 自身の使用箇所すべて(テストとテンプレート内容、計 58 ファイル)が Testing パッケージ経由になり、ソリューションは非推奨警告ゼロでビルドされます。**あなたにお願いしている移行は、我々が先に行った移行です。**

## こちらでは分からないこと

**このリポジトリ内**の二引数使用は列挙できますが、**あなたのコードは列挙できません。** 重要な環境で `InMemoryDcbExecutor` を永続ストアと組み合わせて動かしているなら、上の grep がそれを見つけます。太字の行がその意味です。ただしそれは**あなたの grep** であり、他の誰にも代行できません。本番ガードが存在するのは、まさにこの種の問題が「名前を読む」ことでは発見できないからです。

## 関連

- [ストレージプロバイダーと本番ガード](11_storage_providers.md)
- [ユニットテスト](12_unit_testing.md)
- [Orleans セットアップ](10_orleans_setup.md)
