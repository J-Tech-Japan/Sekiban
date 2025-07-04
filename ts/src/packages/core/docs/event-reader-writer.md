# Event Reader/Writer Implementation

This document describes the TypeScript implementation of the powerful event reader/writer interfaces that match the C# Sekiban implementation.

## Key Components

### 1. IEventReader and IEventWriter Interfaces

These simple yet powerful interfaces replace the old IEventStorageProvider:

```typescript
// IEventReader - for reading events
interface IEventReader {
  getEvents(eventRetrievalInfo: EventRetrievalInfo): ResultAsync<readonly IEvent[], Error>;
}

// IEventWriter - for writing events
interface IEventWriter {
  saveEvents<TEvent extends IEvent>(events: TEvent[]): Promise<void>;
}
```

### 2. EventRetrievalInfo

The `EventRetrievalInfo` class provides a powerful and flexible way to query events:

- **Filtering by partition**: Root partition key, aggregate stream (group), and aggregate ID
- **Sortable ID conditions**: None, Since, and Between conditions for temporal filtering
- **Result limiting**: Optional max count for pagination

Key features:
- Uses `OptionalValue<T>` wrapper for nullable values
- Supports complex filtering scenarios
- Type-safe and composable

### 3. Sortable ID Conditions

Three types of conditions for filtering events by their sortable IDs:

- `SortableIdConditionNone`: No filtering (returns all events)
- `SinceSortableIdCondition`: Returns events after a specific ID
- `BetweenSortableIdCondition`: Returns events within a range

### 4. Aggregate Streams

The `IAggregatesStream` interface and `AggregateGroupStream` implementation provide a way to group aggregates logically.

## Migration from Old Interface

The old `IEventStorageProvider` interface with methods like:
- `loadEventsByPartitionKey()`
- `loadEvents()` 
- `saveEvents()` with EventBatch
- Snapshot-related methods

Has been replaced with the simpler and more powerful:
- `IEventReader.getEvents()` with `EventRetrievalInfo`
- `IEventWriter.saveEvents()` with direct event array

**Note**: All snapshot functionality has been removed as requested. Snapshots should be handled at a higher level if needed.

## Example Usage

```typescript
// Get all events
const allEvents = await eventStore.getEvents(EventRetrievalInfo.all());

// Get events for a specific aggregate
const aggregateEvents = await eventStore.getEvents(
  EventRetrievalInfo.fromPartitionKeys(partitionKeys)
);

// Get events with complex filtering
const filteredEvents = await eventStore.getEvents(
  new EventRetrievalInfo(
    OptionalValue.fromValue('tenant1'),
    OptionalValue.fromValue(new AggregateGroupStream('Orders')),
    OptionalValue.empty(),
    SortableIdCondition.since(lastProcessedId),
    OptionalValue.fromValue(100) // max 100 events
  )
);

// Save events
await eventStore.saveEvents(newEvents);
```

## Benefits

1. **Simplicity**: Clean, focused interfaces that do one thing well
2. **Power**: EventRetrievalInfo enables complex queries without complex APIs
3. **Type Safety**: Full TypeScript type safety with Result types
4. **Flexibility**: Easy to extend with new conditions or filters
5. **Performance**: Implementations can optimize based on the retrieval info