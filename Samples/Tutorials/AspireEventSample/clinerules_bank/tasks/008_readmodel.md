clinerules_bank/tasks/007_readmodel_handler.md
で Readmodel Handlerの設計をしています。

結構良くなってきましたが、いくつか追加

- IEntityWriter<> には　GetLastSortableUniqueIdForGroupAsync は不要
- ReadModelHandlerBase は不要 各ReadModelは、switch でイベントに対して GetPayload() をして、その型によって 各EntityWriterから読んだり、書き込んだりする

上記をもとによく考慮して、再度
clinerules_bank/tasks/008_readmodel.md
に設計を再度追記してください。
+++++++++++++

# さらに改善された設計: シンプルなReadModelハンドラーアプローチ

前回の設計をさらに簡素化し、フィードバックを反映した設計を提案します。

## 1. 基本的な考え方

- ReadModelタイプごとにハンドラーを作成する
- ハンドラーはコンストラクタインジェクションで必要なリポジトリを受け取る
- 基底クラスは使わず、各ハンドラーが直接インターフェースを実装する
- イベントの種類に応じてswitch文で処理を分岐する
- リポジトリは最後に処理したイベントのSortableUniqueIdを追跡する

## 2. 主要なインターフェースとクラス

### 2.1 エンティティライターインターフェース

```csharp
/// <summary>
/// 読み取りモデルエンティティの保存と取得を行うインターフェース
/// </summary>
public interface IEntityWriter<TEntity> where TEntity : IReadModelEntity
{
    /// <summary>
    /// 指定されたIDのエンティティを取得
    /// </summary>
    Task<TEntity?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId);
    
    /// <summary>
    /// 指定されたIDのエンティティの履歴を取得
    /// </summary>
    Task<List<TEntity>> GetHistoryEntityByIdAsync(
        string rootPartitionKey, 
        string aggregateGroup, 
        Guid targetId, 
        string beforeSortableUniqueId);
    
    /// <summary>
    /// エンティティを追加または更新
    /// </summary>
    Task<TEntity> AddOrUpdateEntityAsync(TEntity entity);
    
    /// <summary>
    /// 最後に処理したイベントのSortableUniqueIdを取得
    /// </summary>
    Task<string> GetLastSortableUniqueIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId);
}
```

### 2.2 ReadModelハンドラーインターフェース

```csharp
/// <summary>
/// 特定のReadModelタイプを処理するハンドラーのインターフェース
/// </summary>
public interface IReadModelHandler
{
    /// <summary>
    /// イベントを処理する
    /// </summary>
    Task HandleEventAsync(IEvent @event);
}
```

### 2.3 イベントコンテキストクラス

```csharp
/// <summary>
/// イベントのコンテキスト情報を保持するクラス
/// </summary>
public class EventContext
{
    /// <summary>
    /// イベント
    /// </summary>
    public IEvent Event { get; }
    
    /// <summary>
    /// ルートパーティションキー
    /// </summary>
    public string RootPartitionKey => Event.PartitionKeys.RootPartitionKey;
    
    /// <summary>
    /// アグリゲートグループ
    /// </summary>
    public string AggregateGroup => Event.PartitionKeys.Group;
    
    /// <summary>
    /// ターゲットID
    /// </summary>
    public Guid TargetId => Event.PartitionKeys.AggregateId;
    
    /// <summary>
    /// ソータブルユニークID
    /// </summary>
    public string SortableUniqueId => Event.SortableUniqueId;
    
    public EventContext(IEvent @event)
    {
        Event = @event;
    }
}
```

### 2.4 イベントコンテキストプロバイダー

```csharp
/// <summary>
/// 現在処理中のイベントのコンテキスト情報を提供するインターフェース
/// </summary>
public interface IEventContextProvider
{
    /// <summary>
    /// 現在処理中のイベントのコンテキスト情報を取得
    /// </summary>
    EventContext GetCurrentEventContext();
    
    /// <summary>
    /// イベントコンテキストを設定
    /// </summary>
    void SetCurrentEventContext(IEvent @event);
    
    /// <summary>
    /// イベントコンテキストをクリア
    /// </summary>
    void ClearCurrentEventContext();
}

/// <summary>
/// イベントコンテキストプロバイダーの実装
/// </summary>
public class EventContextProvider : IEventContextProvider
{
    private readonly AsyncLocal<EventContext> _currentEventContext = new();
    
    public EventContext GetCurrentEventContext()
    {
        var context = _currentEventContext.Value;
        if (context == null)
        {
            throw new InvalidOperationException("No event is being processed currently");
        }
        
        return context;
    }
    
    public void SetCurrentEventContext(IEvent @event)
    {
        _currentEventContext.Value = new EventContext(@event);
    }
    
    public void ClearCurrentEventContext()
    {
        _currentEventContext.Value = null;
    }
}
```

### 2.5 イベントプロセッサー

```csharp
/// <summary>
/// イベント処理を調整する中心的なクラス
/// </summary>
public class EventProcessor
{
    private readonly IEnumerable<IReadModelHandler> _handlers;
    private readonly IEventContextProvider _eventContextProvider;
    private readonly ILogger<EventProcessor> _logger;
    
    public EventProcessor(
        IEnumerable<IReadModelHandler> handlers,
        IEventContextProvider eventContextProvider,
        ILogger<EventProcessor> logger)
    {
        _handlers = handlers;
        _eventContextProvider = eventContextProvider;
        _logger = logger;
    }
    
    /// <summary>
    /// 単一イベントを処理
    /// </summary>
    public async Task ProcessEventAsync(IEvent @event)
    {
        try
        {
            // イベントコンテキストを設定
            _eventContextProvider.SetCurrentEventContext(@event);
            
            // すべてのハンドラーでイベントを処理
            var tasks = _handlers.Select(h => ProcessEventWithHandlerAsync(h, @event));
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing event {EventType} with ID {EventId}",
                @event.GetPayload().GetType().Name, @event.PartitionKeys.AggregateId);
            throw;
        }
        finally
        {
            // 処理が完了したらコンテキストをクリア
            _eventContextProvider.ClearCurrentEventContext();
        }
    }
    
    /// <summary>
    /// 単一のハンドラーでイベントを処理し、エラーをログに記録
    /// </summary>
    private async Task ProcessEventWithHandlerAsync(IReadModelHandler handler, IEvent @event)
    {
        try
        {
            await handler.HandleEventAsync(@event);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in handler {HandlerType} processing event {EventType} with ID {EventId}",
                handler.GetType().Name, @event.GetPayload().GetType().Name, @event.PartitionKeys.AggregateId);
            
            // ここでエラー処理ポリシーを適用できる
            // 例: 再試行、スキップ、例外を再スロー、など
            throw;
        }
    }
    
    /// <summary>
    /// イベントのバッチを処理
    /// </summary>
    public async Task ProcessEventsAsync(IEnumerable<IEvent> events)
    {
        foreach (var @event in events)
        {
            await ProcessEventAsync(@event);
        }
    }
}
```

### 2.6 具体的なReadModelハンドラー実装

```csharp
/// <summary>
/// Branch ReadModelを処理するハンドラー
/// </summary>
public class BranchReadModelHandler : IReadModelHandler
{
    private readonly IBranchWriter _branchWriter;
    private readonly IEventContextProvider _eventContextProvider;
    private readonly ILogger<BranchReadModelHandler> _logger;
    
    public BranchReadModelHandler(
        IBranchWriter branchWriter,
        IEventContextProvider eventContextProvider,
        ILogger<BranchReadModelHandler> logger)
    {
        _branchWriter = branchWriter;
        _eventContextProvider = eventContextProvider;
        _logger = logger;
    }
    
    /// <summary>
    /// イベントを処理
    /// </summary>
    public async Task HandleEventAsync(IEvent @event)
    {
        var eventPayload = @event.GetPayload();
        
        // イベントタイプに基づいて処理を分岐
        switch (eventPayload)
        {
            case BranchCreated branchCreated:
                await HandleBranchCreatedAsync(branchCreated);
                break;
                
            case BranchNameChanged branchNameChanged:
                await HandleBranchNameChangedAsync(branchNameChanged);
                break;
                
            // 他のイベントタイプも同様に処理
            // このハンドラーが処理しないイベントタイプは無視される
        }
    }
    
    /// <summary>
    /// BranchCreatedイベントを処理
    /// </summary>
    private async Task HandleBranchCreatedAsync(BranchCreated @event)
    {
        var context = _eventContextProvider.GetCurrentEventContext();
        
        _logger.LogInformation("Processing BranchCreated event for branch {BranchName} with ID {BranchId}",
            @event.Name, context.TargetId);
        
        var entity = new BranchDbRecord
        {
            Id = Guid.NewGuid(),
            TargetId = context.TargetId,
            RootPartitionKey = context.RootPartitionKey,
            AggregateGroup = context.AggregateGroup,
            LastSortableUniqueId = context.SortableUniqueId,
            TimeStamp = DateTime.UtcNow,
            Name = @event.Name,
            Country = @event.Country
        };
        
        await _branchWriter.AddOrUpdateEntityAsync(entity);
    }
    
    /// <summary>
    /// BranchNameChangedイベントを処理
    /// </summary>
    private async Task HandleBranchNameChangedAsync(BranchNameChanged @event)
    {
        var context = _eventContextProvider.GetCurrentEventContext();
        
        _logger.LogInformation("Processing BranchNameChanged event for branch with ID {BranchId}, new name: {BranchName}",
            context.TargetId, @event.Name);
        
        var existing = await _branchWriter.GetEntityByIdAsync(
            context.RootPartitionKey, 
            context.AggregateGroup, 
            context.TargetId);
            
        if (existing != null)
        {
            existing.LastSortableUniqueId = context.SortableUniqueId;
            existing.TimeStamp = DateTime.UtcNow;
            existing.Name = @event.Name;
            
            await _branchWriter.AddOrUpdateEntityAsync(existing);
        }
        else
        {
            _logger.LogWarning("Branch with ID {BranchId} not found when processing BranchNameChanged event",
                context.TargetId);
        }
    }
}

/// <summary>
/// ShoppingCart ReadModelを処理するハンドラー
/// </summary>
public class ShoppingCartReadModelHandler : IReadModelHandler
{
    private readonly ICartEntityWriter _inMemoryWriter;
    private readonly ICartEntityPostgresWriter _postgresWriter;
    private readonly IEventContextProvider _eventContextProvider;
    private readonly ILogger<ShoppingCartReadModelHandler> _logger;
    
    public ShoppingCartReadModelHandler(
        ICartEntityWriter inMemoryWriter,
        ICartEntityPostgresWriter postgresWriter,
        IEventContextProvider eventContextProvider,
        ILogger<ShoppingCartReadModelHandler> logger)
    {
        _inMemoryWriter = inMemoryWriter;
        _postgresWriter = postgresWriter;
        _eventContextProvider = eventContextProvider;
        _logger = logger;
    }
    
    /// <summary>
    /// イベントを処理
    /// </summary>
    public async Task HandleEventAsync(IEvent @event)
    {
        var eventPayload = @event.GetPayload();
        
        // イベントタイプに基づいて処理を分岐
        switch (eventPayload)
        {
            case ShoppingCartCreated shoppingCartCreated:
                await HandleShoppingCartCreatedAsync(shoppingCartCreated);
                break;
                
            case ShoppingCartItemAdded shoppingCartItemAdded:
                await HandleShoppingCartItemAddedAsync(shoppingCartItemAdded);
                break;
                
            case ShoppingCartPaymentProcessed shoppingCartPaymentProcessed:
                await HandleShoppingCartPaymentProcessedAsync(shoppingCartPaymentProcessed);
                break;
                
            // 他のイベントタイプも同様に処理
            // このハンドラーが処理しないイベントタイプは無視される
        }
    }
    
    /// <summary>
    /// ShoppingCartCreatedイベントを処理
    /// </summary>
    private async Task HandleShoppingCartCreatedAsync(ShoppingCartCreated @event)
    {
        var context = _eventContextProvider.GetCurrentEventContext();
        
        _logger.LogInformation("Processing ShoppingCartCreated event for user {UserId} with cart ID {CartId}",
            @event.UserId, context.TargetId);
        
        // インメモリエンティティを作成
        var inMemoryEntity = new CartEntity
        {
            Id = Guid.NewGuid(),
            TargetId = context.TargetId,
            RootPartitionKey = context.RootPartitionKey,
            AggregateGroup = context.AggregateGroup,
            LastSortableUniqueId = context.SortableUniqueId,
            TimeStamp = DateTime.UtcNow,
            UserId = @event.UserId,
            Items = new List<ShoppingCartItems>(),
            Status = "Created",
            TotalAmount = 0
        };
        
        // Postgresエンティティを作成
        var postgresEntity = new CartDbRecord
        {
            Id = Guid.NewGuid(),
            TargetId = context.TargetId,
            RootPartitionKey = context.RootPartitionKey,
            AggregateGroup = context.AggregateGroup,
            LastSortableUniqueId = context.SortableUniqueId,
            TimeStamp = DateTime.UtcNow,
            UserId = @event.UserId,
            Status = "Created",
            TotalAmount = 0,
            ItemsJson = "[]"
        };
        
        // 両方のリポジトリに保存
        await Task.WhenAll(
            _inMemoryWriter.AddOrUpdateEntityAsync(inMemoryEntity),
            _postgresWriter.AddOrUpdateEntityAsync(postgresEntity)
        );
    }
    
    /// <summary>
    /// ShoppingCartItemAddedイベントを処理
    /// </summary>
    private async Task HandleShoppingCartItemAddedAsync(ShoppingCartItemAdded @event)
    {
        var context = _eventContextProvider.GetCurrentEventContext();
        
        _logger.LogInformation("Processing ShoppingCartItemAdded event for cart ID {CartId}, item: {ItemName}",
            context.TargetId, @event.Name);
        
        // インメモリエンティティを更新
        var inMemoryEntity = await _inMemoryWriter.GetEntityByIdAsync(
            context.RootPartitionKey,
            context.AggregateGroup,
            context.TargetId);
            
        if (inMemoryEntity != null)
        {
            var updatedItems = new List<ShoppingCartItems>(inMemoryEntity.Items)
            {
                new(@event.Name, @event.Quantity, @event.ItemId, @event.Price)
            };
            
            var totalAmount = updatedItems.Sum(item => item.Price * item.Quantity);
            
            var updatedInMemoryEntity = inMemoryEntity with
            {
                LastSortableUniqueId = context.SortableUniqueId,
                TimeStamp = DateTime.UtcNow,
                Items = updatedItems,
                TotalAmount = totalAmount
            };
            
            await _inMemoryWriter.AddOrUpdateEntityAsync(updatedInMemoryEntity);
        }
        
        // Postgresエンティティを更新
        var postgresEntity = await _postgresWriter.GetEntityByIdAsync(
            context.RootPartitionKey,
            context.AggregateGroup,
            context.TargetId);
            
        if (postgresEntity != null)
        {
            // 既存のアイテムをJSONから取得
            List<ShoppingCartItems> items;
            if (string.IsNullOrEmpty(postgresEntity.ItemsJson) || postgresEntity.ItemsJson == "[]")
            {
                items = new List<ShoppingCartItems>();
            }
            else
            {
                items = JsonSerializer.Deserialize<List<ShoppingCartItems>>(postgresEntity.ItemsJson) ??
                    new List<ShoppingCartItems>();
            }
            
            // 新しいアイテムを追加
            items.Add(new ShoppingCartItems(@event.Name, @event.Quantity, @event.ItemId, @event.Price));
            
            // 合計金額を計算
            var totalAmount = items.Sum(item => item.Price * item.Quantity);
            
            // エンティティを更新
            postgresEntity.LastSortableUniqueId = context.SortableUniqueId;
            postgresEntity.TimeStamp = DateTime.UtcNow;
            postgresEntity.ItemsJson = JsonSerializer.Serialize(items);
            postgresEntity.TotalAmount = totalAmount;
            
            await _postgresWriter.AddOrUpdateEntityAsync(postgresEntity);
        }
    }
    
    /// <summary>
    /// ShoppingCartPaymentProcessedイベントを処理
    /// </summary>
    private async Task HandleShoppingCartPaymentProcessedAsync(ShoppingCartPaymentProcessed @event)
    {
        var context = _eventContextProvider.GetCurrentEventContext();
        
        _logger.LogInformation("Processing ShoppingCartPaymentProcessed event for cart ID {CartId}",
            context.TargetId);
        
        // インメモリエンティティを更新
        var inMemoryEntity = await _inMemoryWriter.GetEntityByIdAsync(
            context.RootPartitionKey,
            context.AggregateGroup,
            context.TargetId);
            
        if (inMemoryEntity != null)
        {
            var updatedInMemoryEntity = inMemoryEntity with
            {
                LastSortableUniqueId = context.SortableUniqueId,
                TimeStamp = DateTime.UtcNow,
                Status = "Paid"
            };
            
            await _inMemoryWriter.AddOrUpdateEntityAsync(updatedInMemoryEntity);
        }
        
        // Postgresエンティティを更新
        var postgresEntity = await _postgresWriter.GetEntityByIdAsync(
            context.RootPartitionKey,
            context.AggregateGroup,
            context.TargetId);
            
        if (postgresEntity != null)
        {
            postgresEntity.LastSortableUniqueId = context.SortableUniqueId;
            postgresEntity.TimeStamp = DateTime.UtcNow;
            postgresEntity.Status = "Paid";
            
            await _postgresWriter.AddOrUpdateEntityAsync(postgresEntity);
        }
    }
}
```

### 2.7 エンティティライターの実装例

```csharp
/// <summary>
/// Postgresを使用したBranchDbRecordライター
/// </summary>
public class BranchEntityPostgresWriter : IBranchWriter
{
    private readonly BranchDbContext _dbContext;
    private readonly ILogger<BranchEntityPostgresWriter> _logger;
    
    public BranchEntityPostgresWriter(
        BranchDbContext dbContext,
        ILogger<BranchEntityPostgresWriter> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }
    
    public async Task<BranchDbRecord?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        var record = await _dbContext
            .Branches
            .Where(
                e => e.RootPartitionKey == rootPartitionKey &&
                    e.AggregateGroup == aggregateGroup &&
                    e.TargetId == targetId)
            .OrderByDescending(e => e.TimeStamp)
            .FirstOrDefaultAsync();
        return record;
    }
    
    public async Task<List<BranchDbRecord>> GetHistoryEntityByIdAsync(
        string rootPartitionKey,
        string aggregateGroup,
        Guid targetId,
        string beforeSortableUniqueId)
    {
        var records = await _dbContext
            .Branches
            .Where(
                e => e.RootPartitionKey == rootPartitionKey &&
                    e.AggregateGroup == aggregateGroup &&
                    e.TargetId == targetId &&
                    e.LastSortableUniqueId.CompareTo(beforeSortableUniqueId) < 0)
            .OrderByDescending(e => e.TimeStamp)
            .ToListAsync();

        return records;
    }
    
    public async Task<BranchDbRecord> AddOrUpdateEntityAsync(BranchDbRecord entity)
    {
        try
        {
            // Check if the entity already exists
            var existingEntity = await _dbContext.Branches.FindAsync(entity.Id);

            if (existingEntity == null)
            {
                // Add new entity
                _logger.LogDebug("Adding new branch entity with ID {BranchId}, name: {BranchName}",
                    entity.TargetId, entity.Name);
                    
                await _dbContext.Branches.AddAsync(entity);
            }
            else
            {
                // Update existing entity
                _logger.LogDebug("Updating branch entity with ID {BranchId}, name: {BranchName}",
                    entity.TargetId, entity.Name);
                    
                _dbContext.Branches.Remove(existingEntity);
                await _dbContext.Branches.AddAsync(entity);
            }

            await _dbContext.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving branch entity with ID {BranchId}", entity.TargetId);
            throw;
        }
    }
    
    public async Task<string> GetLastSortableUniqueIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        var record = await _dbContext
            .Branches
            .Where(
                e => e.RootPartitionKey == rootPartitionKey &&
                    e.AggregateGroup == aggregateGroup &&
                    e.TargetId == targetId)
            .OrderByDescending(e => e.TimeStamp)
            .FirstOrDefaultAsync();
            
        return record?.LastSortableUniqueId ?? string.Empty;
    }
}
```

### 2.8 アダプター

```csharp
/// <summary>
/// Orleansストリームからイベントを取得するアダプター
/// </summary>
public class OrleansStreamEventSourceAdapter
{
    private readonly EventProcessor _eventProcessor;
    private readonly ILogger<OrleansStreamEventSourceAdapter> _logger;
    
    public OrleansStreamEventSourceAdapter(
        EventProcessor eventProcessor,
        ILogger<OrleansStreamEventSourceAdapter> logger)
    {
        _eventProcessor = eventProcessor;
        _logger = logger;
    }
    
    /// <summary>
    /// Orleansストリームからのイベントを処理
    /// </summary>
    public Task ProcessStreamEventAsync(IEvent @event, StreamSequenceToken? token)
    {
        _logger.LogDebug("Processing stream event {EventType} with ID {EventId}",
            @event.GetPayload().GetType().Name, @event.PartitionKeys.AggregateId);
            
        return _eventProcessor.ProcessEventAsync(@event);
    }
}

/// <summary>
/// コンソールアプリケーション用のイベントソースアダプター
/// </summary>
public class ConsoleAppEventSourceAdapter
{
    private readonly EventProcessor _eventProcessor;
    private readonly IEventStore _eventStore;
    private readonly ILogger<ConsoleAppEventSourceAdapter> _logger;
    
    public ConsoleAppEventSourceAdapter(
        EventProcessor eventProcessor,
        IEventStore eventStore,
        ILogger<ConsoleAppEventSourceAdapter> logger)
    {
        _eventProcessor = eventProcessor;
        _eventStore = eventStore;
        _logger = logger;
    }
    
    /// <summary>
    /// 特定の時点からすべてのイベントを処理
    /// </summary>
    public async Task ProcessEventsFromPointAsync(string fromSortableUniqueId)
    {
        _logger.LogInformation("Processing events from SortableUniqueId: {SortableUniqueId}", fromSortableUniqueId);
        
        var events = await _eventStore.GetEventsFromAsync(fromSortableUniqueId);
        var eventCount = events.Count();
        
        _logger.LogInformation("Found {EventCount} events to process", eventCount);
        
        await _eventProcessor.ProcessEventsAsync(events);
        
        _logger.LogInformation("Finished processing {EventCount} events", eventCount);
    }
    
    /// <summary>
    /// すべてのイベントを最初から処理
    /// </summary>
    public async Task ProcessAllEventsAsync()
    {
        _logger.LogInformation("Processing all events from the beginning");
        
        var events = await _eventStore.GetAllEventsAsync();
        var eventCount = events.Count();
        
        _logger.LogInformation("Found {EventCount} events to process", eventCount);
        
        await _eventProcessor.ProcessEventsAsync(events);
        
        _logger.LogInformation("Finished processing {EventCount} events", eventCount);
    }
    
    /// <summary>
    /// 特定のエンティティの最後に処理したイベントから処理を再開
    /// </summary>
    public async Task ResumeProcessingForEntityAsync<TEntity>(
        IEntityWriter<TEntity> entityWriter,
        string rootPartitionKey,
        string aggregateGroup,
        Guid targetId)
        where TEntity : IReadModelEntity
    {
        var lastSortableUniqueId = await entityWriter.GetLastSortableUniqueIdAsync(rootPartitionKey, aggregateGroup, targetId);
        
        if (string.IsNullOrEmpty(lastSortableUniqueId))
        {
            _logger.LogInformation("No previous events found for entity {TargetId}, processing all events", targetId);
            await ProcessAllEventsAsync();
        }
        else
        {
            _logger.LogInformation("Resuming processing for entity {TargetId} from SortableUniqueId: {SortableUniqueId}",
                targetId, lastSortableUniqueId);
            await ProcessEventsFromPointAsync(lastSortableUniqueId);
        }
    }
}
```

## 3. 修正後の`EventConsumerGrain`

```csharp
[ImplicitStreamSubscription("AllEvents")]
public class EventConsumerGrain : Grain, IEventConsumerGrain
{
    private readonly OrleansStreamEventSourceAdapter _adapter;
    private readonly ILogger<EventConsumerGrain> _logger;
    private IAsyncStream<IEvent>? _stream;
    private StreamSubscriptionHandle<IEvent>? _subscriptionHandle;
    
    public EventConsumerGrain(
        OrleansStreamEventSourceAdapter adapter,
        ILogger<EventConsumerGrain> logger)
    {
        _adapter = adapter;
        _logger = logger;
    }
    
    public Task OnErrorAsync(Exception ex)
    {
        _logger.LogError(ex, "Error in event stream");
        return Task.CompletedTask;
    }
    
    // アダプターを使用してイベントを処理
    public Task OnNextAsync(IEvent item, StreamSequenceToken? token)
    {
        _logger.LogDebug("Processing event {EventType} with ID {EventId}",
            item.GetPayload().GetType().Name, item.PartitionKeys.AggregateId);
            
        return _adapter.ProcessStreamEventAsync(item, token);
    }
    
    public Task OnCompletedAsync()
    {
        _logger.LogInformation("Event stream completed");
        return Task.CompletedTask;
    }
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
以下省略