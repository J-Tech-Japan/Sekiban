AspireEventSample.ApiService/Grains/EventConsumerGrain.cs
をリファクタリングしたい。

OnNextAsync の中で行っていることを、できれば汎用化して純粋関数化したい。

それによって、同じコードを使い回して、Readmodel Updatorを実行できるようにしたい

今やっている、OrleansのイベントストリーミングでRead Model を作成する方法
かつ、コンソールアプリで最初のイベントから今まで、もしくは過去の何処かから今までと実行できるようにしたい。

それを行うための抽象化を行いたいです。

つまり純粋関数を持つクラスを作り、

AspireEventSample.ApiService/Grains/EventConsumerGrain.cs
から呼び出す機能を作る。

ただ、このタスクでは計画するだけです。
このファイルの下部に計画をよく考えて記入してください。必要なファイルの読み込みなど調査を行い、できるだけ具体的に計画してください。

+++++++++++以下に計画を書く+++++++++++

# リファクタリング計画: EventConsumerGrainの抽象化と純粋関数化

現在の`EventConsumerGrain.cs`を分析した結果、以下のリファクタリング計画を提案します。この計画では、イベント処理ロジックを純粋関数として抽出し、Orleans Grainシステムとコンソールアプリケーションの両方で再利用できるようにします。

## 1. 新しいクラス構造

### 1.1 イベントハンドラーインターフェース

```csharp
// イベントハンドラーの基本インターフェース
public interface IEventHandler<TEvent, TReadModel>
{
    // イベントを処理して読み取りモデルを更新する純粋関数
    Task<TReadModel> HandleEventAsync(TEvent @event, TReadModel? existingModel, string sortableUniqueId);
}
```

### 1.2 読み取りモデルリポジトリインターフェース

```csharp
// 読み取りモデルの保存と取得を抽象化するインターフェース
public interface IReadModelRepository<TReadModel> where TReadModel : IReadModelEntity
{
    Task<TReadModel?> GetByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId);
    Task<List<TReadModel>> GetHistoryByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId, string beforeSortableUniqueId);
    Task<TReadModel> SaveAsync(TReadModel entity);
}
```

### 1.3 イベントプロセッサー

```csharp
// イベント処理を調整する中心的なクラス
public class EventProcessor
{
    private readonly Dictionary<Type, object> _eventHandlers = new();
    private readonly Dictionary<Type, object> _readModelRepositories = new();

    // イベントハンドラーを登録するメソッド
    public void RegisterEventHandler<TEvent, TReadModel>(
        IEventHandler<TEvent, TReadModel> handler,
        IReadModelRepository<TReadModel> repository)
        where TReadModel : IReadModelEntity
    {
        _eventHandlers[typeof(TEvent)] = handler;
        _readModelRepositories[typeof(TReadModel)] = repository;
    }

    // 単一イベントを処理するメソッド
    public async Task ProcessEventAsync(IEvent @event)
    {
        var eventType = @event.GetPayload().GetType();
        var eventPayload = @event.GetPayload();
        
        // 適切なハンドラーとリポジトリを見つける
        if (_eventHandlers.TryGetValue(eventType, out var handlerObj))
        {
            // 動的に適切なハンドラーとリポジトリを呼び出す
            // 実装の詳細は省略
        }
    }

    // イベントのバッチを処理するメソッド
    public async Task ProcessEventsAsync(IEnumerable<IEvent> events)
    {
        foreach (var @event in events)
        {
            await ProcessEventAsync(@event);
        }
    }
}
```

### 1.4 具体的なイベントハンドラー実装

```csharp
// BranchCreatedイベントのハンドラー
public class BranchCreatedHandler : IEventHandler<BranchCreated, BranchDbRecord>
{
    public Task<BranchDbRecord> HandleEventAsync(
        BranchCreated @event, 
        BranchDbRecord? existingModel, 
        string sortableUniqueId)
    {
        var entity = existingModel ?? new BranchDbRecord
        {
            Id = Guid.NewGuid(),
            TargetId = /* targetId from context */,
            RootPartitionKey = /* from context */,
            AggregateGroup = /* from context */
        };
        
        entity.LastSortableUniqueId = sortableUniqueId;
        entity.TimeStamp = DateTime.UtcNow;
        entity.Name = @event.Name;
        entity.Country = @event.Country;
        
        return Task.FromResult(entity);
    }
}

// 他のイベントハンドラーも同様に実装
```

### 1.5 リポジトリ実装

```csharp
// Postgresを使用したBranchDbRecordリポジトリ
public class BranchDbRecordPostgresRepository : IReadModelRepository<BranchDbRecord>
{
    private readonly BranchDbContext _dbContext;
    
    public BranchDbRecordPostgresRepository(BranchDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    // インターフェースメソッドの実装
    // ...
}

// インメモリCartEntityリポジトリ
public class CartEntityInMemoryRepository : IReadModelRepository<CartEntity>
{
    // インターフェースメソッドの実装
    // ...
}
```

### 1.6 イベントソースアダプター

```csharp
// Orleansストリームからイベントを取得するアダプター
public class OrleansStreamEventSourceAdapter
{
    private readonly EventProcessor _eventProcessor;
    
    public OrleansStreamEventSourceAdapter(EventProcessor eventProcessor)
    {
        _eventProcessor = eventProcessor;
    }
    
    // Orleansストリームからのイベントを処理するメソッド
    public Task ProcessStreamEventAsync(IEvent @event, StreamSequenceToken? token)
    {
        return _eventProcessor.ProcessEventAsync(@event);
    }
}

// コンソールアプリケーション用のイベントソースアダプター
public class ConsoleAppEventSourceAdapter
{
    private readonly EventProcessor _eventProcessor;
    
    public ConsoleAppEventSourceAdapter(EventProcessor eventProcessor)
    {
        _eventProcessor = eventProcessor;
    }
    
    // 特定の時点からすべてのイベントを処理するメソッド
    public async Task ProcessEventsFromPointAsync(
        string fromSortableUniqueId, 
        IEventStore eventStore)
    {
        var events = await eventStore.GetEventsFromAsync(fromSortableUniqueId);
        await _eventProcessor.ProcessEventsAsync(events);
    }
    
    // すべてのイベントを最初から処理するメソッド
    public async Task ProcessAllEventsAsync(IEventStore eventStore)
    {
        var events = await eventStore.GetAllEventsAsync();
        await _eventProcessor.ProcessEventsAsync(events);
    }
}
```

## 2. リファクタリング手順

1. 上記のインターフェースとクラスを作成する
2. 各イベントタイプに対応するハンドラーを実装する
3. 各読み取りモデルタイプに対応するリポジトリを実装する
4. `EventConsumerGrain`を修正して、新しい`EventProcessor`と`OrleansStreamEventSourceAdapter`を使用するようにする
5. コンソールアプリケーション用のエントリポイントを作成する

## 3. 修正後の`EventConsumerGrain`

```csharp
[ImplicitStreamSubscription("AllEvents")]
public class EventConsumerGrain : Grain, IEventConsumerGrain
{
    private readonly OrleansStreamEventSourceAdapter _adapter;
    private IAsyncStream<IEvent>? _stream;
    private StreamSubscriptionHandle<IEvent>? _subscriptionHandle;
    
    public EventConsumerGrain(OrleansStreamEventSourceAdapter adapter)
    {
        _adapter = adapter;
    }
    
    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
    
    // 純粋関数を使用してイベントを処理
    public Task OnNextAsync(IEvent item, StreamSequenceToken? token)
    {
        return _adapter.ProcessStreamEventAsync(item, token);
    }
    
    public Task OnCompletedAsync() => Task.CompletedTask;
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var streamProvider = this.GetStreamProvider("EventStreamProvider");
        _stream = streamProvider.GetStream<IEvent>(StreamId.Create("AllEvents", Guid.Empty));
        
        _subscriptionHandle = await _stream.SubscribeAsync(
            (evt, token) => OnNextAsync(evt, token),
            OnErrorAsync,
            OnCompletedAsync
        );
        
        await base.OnActivateAsync(cancellationToken);
    }
}
```

## 4. 依存関係の登録

```csharp
// Program.csでの依存関係の登録
services.AddSingleton<EventProcessor>();

// イベントハンドラーの登録
services.AddTransient<IEventHandler<BranchCreated, BranchDbRecord>, BranchCreatedHandler>();
services.AddTransient<IEventHandler<BranchNameChanged, BranchDbRecord>, BranchNameChangedHandler>();
// 他のイベントハンドラーも同様に登録

// リポジトリの登録
services.AddTransient<IReadModelRepository<BranchDbRecord>, BranchDbRecordPostgresRepository>();
services.AddTransient<IReadModelRepository<CartEntity>, CartEntityInMemoryRepository>();
// 他のリポジトリも同様に登録

// アダプターの登録
services.AddTransient<OrleansStreamEventSourceAdapter>();
services.AddTransient<ConsoleAppEventSourceAdapter>();

// イベントハンドラーとリポジトリの設定を行うコード
services.AddTransient<IStartupTask, EventProcessorSetup>();
```

## 5. コンソールアプリケーションの例

```csharp
// コンソールアプリケーションのエントリポイント
public class Program
{
    public static async Task Main(string[] args)
    {
        // 依存関係の設定
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();
        
        // イベントプロセッサーとアダプターの取得
        var adapter = serviceProvider.GetRequiredService<ConsoleAppEventSourceAdapter>();
        var eventStore = serviceProvider.GetRequiredService<IEventStore>();
        
        // コマンドライン引数に基づいて処理方法を決定
        if (args.Length > 0 && args[0] == "--from")
        {
            // 特定の時点からイベントを処理
            await adapter.ProcessEventsFromPointAsync(args[1], eventStore);
        }
        else
        {
            // すべてのイベントを処理
            await adapter.ProcessAllEventsAsync(eventStore);
        }
    }
    
    private static void ConfigureServices(IServiceCollection services)
    {
        // 依存関係の登録（上記と同様）
    }
}
```

## 6. 考慮事項と注意点

1. **コンテキスト情報の受け渡し**: イベントハンドラーには、イベントペイロードだけでなく、パーティションキーやアグリゲートIDなどのコンテキスト情報も必要です。これを効率的に渡す方法を検討する必要があります。

2. **トランザクション管理**: 複数のリポジトリにまたがる更新がある場合、トランザクション管理を考慮する必要があります。

3. **エラー処理とリトライ**: イベント処理中にエラーが発生した場合の処理とリトライ戦略を実装する必要があります。

4. **パフォーマンス最適化**: 大量のイベントを処理する場合、バッチ処理やパラレル処理の導入を検討する必要があります。

5. **テスト容易性**: 純粋関数化することで、ユニットテストが容易になります。各イベントハンドラーに対する包括的なテストを作成することを検討してください。
