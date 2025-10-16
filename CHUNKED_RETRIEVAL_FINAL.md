# IndexedDB Chunked Retrieval - Final Implementation

## Problem
The application was experiencing Out of Memory (OOM) errors when retrieving large datasets from IndexedDB because all events were loaded into memory at once.

## Previous Approach (Failed)
Initially attempted to return all chunks as a 2D array `DbEvent[][]`, but this still caused OOM because:
- The entire 2D array was serialized as a single JSON payload
- `JsonSerializer.Deserialize[DbEvent[][]]` loaded all chunks into memory simultaneously
- Memory usage was essentially the same as loading all events at once

## Current Solution
Implemented true one-chunk-at-a-time retrieval using skip/offset pattern:

### Key Changes

#### 1. TypeScript Runtime (`index.ts`)
- Added `filterEventsChunk()` function that accepts `skip` parameter
- Returns a single chunk (slice) of events: `items.slice(skip, skip + chunkSize)`
- Returns empty array when no more events available
- Functions: `getEventsAsyncChunk()` and `getDissolvableEventsAsyncChunk()`

```typescript
const filterEventsChunk = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
	query: DbEventQuery,
	chunkSize: number,
	skip: number,
): Promise<DbEvent[]> => {
	// ... filtering logic ...
	const start = skip;
	const end = skip + chunkSize;
	return items.slice(start, end);
};
```

#### 2. C# Interface (`ISekibanIndexedDbContext.cs`)
- Changed from `GetEventsAsyncChunked()` returning `DbEvent[][]`
- To `GetEventsAsyncChunk()` returning `DbEvent[]` with `skip` parameter
- Signature: `Task<DbEvent[]> GetEventsAsyncChunk(DbEventQuery query, int chunkSize, int skip)`

#### 3. C# Implementation (`AbstractSekibanIndexedDbContext.cs`)
- Calls JavaScript `getEventsAsyncChunk` with skip parameter
- Returns non-nullable `DbEvent[]` (empty array instead of null when done)

#### 4. Repository (`IndexedDbDocumentRepository.cs`)
- Implemented loop that retrieves one chunk at a time
- Increments `skip` by `chunkSize` after each iteration
- Stops when empty array returned
- Calls `resultAction` for each chunk incrementally

```csharp
int skip = 0;
while (true)
{
    var dbEventChunk = await dbContext.GetEventsAsyncChunk(query, chunkSize, skip);
    
    if (dbEventChunk.Length == 0)
    {
        break; // No more events
    }
    
    // Process chunk
    var events = dbEventChunk
        .Select(x => x.ToEvent(registeredEventTypes))
        .OfType<IEvent>();
    
    resultAction(events);
    
    skip += chunkSize; // Move to next chunk
}
```

## Memory Benefits

### Before
```
Total Events: 10,000
Memory Usage: 10,000 events × ~1KB = ~10MB all at once
Risk: OutOfMemoryException
```

### After
```
Total Events: 10,000
Chunk Size: 1,000
Memory Usage: 1,000 events × ~1KB = ~1MB per iteration
Peak Memory: ~1MB (constant)
Risk: None - memory is released after each chunk processed
```

## How It Works

1. **C# calls JS with skip=0**: Gets first 1000 events
2. **Process chunk**: Deserialize, convert to IEvent, call resultAction
3. **Release memory**: Chunk goes out of scope, GC can collect
4. **C# calls JS with skip=1000**: Gets next 1000 events
5. **Repeat**: Until empty array returned

## Important Notes

- Each JavaScript call only retrieves a slice of the filtered results
- C# never holds more than one chunk in memory at a time
- Projection consumers receive events incrementally via `resultAction`
- Empty array (not null) signals end of data
- Works with both regular events and dissolvable events

## Files Modified

1. `/src/Sekiban.Infrastructure.IndexedDb/Runtime/src/index.ts`
   - Added `filterEventsChunk()`
   - Added `getEventsAsyncChunk()` and `getDissolvableEventsAsyncChunk()`

2. `/src/Sekiban.Infrastructure.IndexedDb/Databases/ISekibanIndexedDbContext.cs`
   - Changed interface to single-chunk retrieval with skip

3. `/src/Sekiban.Infrastructure.IndexedDb/Databases/AbstractSekibanIndexedDbContext.cs`
   - Updated to call new chunk methods

4. `/src/Sekiban.Infrastructure.IndexedDb/Documents/IndexedDbDocumentRepository.cs`
   - Implemented loop-based chunk retrieval in `GetEvents()`

## Testing Recommendations

1. Test with large datasets (10,000+ events) to verify OOM no longer occurs
2. Monitor memory usage during chunk retrieval
3. Verify all events are retrieved correctly (no missing data)
4. Test with different aggregate types and partition keys
5. Verify projections work correctly with chunked data

## Performance Considerations

- **Pros**: Constant memory usage, no OOM risk
- **Cons**: Multiple JavaScript interop calls (manageable overhead)
- **Chunk Size**: 1000 events is a good balance between performance and memory
- **Trade-off**: Slight increase in processing time vs. guaranteed stability
