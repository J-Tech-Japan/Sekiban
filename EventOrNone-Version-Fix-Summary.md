# EventOrNone.None Version Increment Fix

## Issue Summary
When a command handler returns `EventOrNone.None` (indicating no state change), the aggregate version was incorrectly being incremented.

## Root Cause
The issue was in the Dapr actor implementations (`AggregateActor.cs` and `ProtobufAggregateActor.cs`) where the `_hasUnsavedChanges` flag was being set to `true` unconditionally after command execution, regardless of whether any events were actually produced.

## Fix Applied

### 1. AggregateActor.cs (lines 217-223)
**Before:**
```csharp
_currentAggregate = repository.GetProjectedAggregate(result.Events).UnwrapBox();
_hasUnsavedChanges = true;
```

**After:**
```csharp
_currentAggregate = repository.GetProjectedAggregate(result.Events).UnwrapBox();

// Only mark as changed if events were actually produced
if (result.Events.Count > 0)
{
    _hasUnsavedChanges = true;
}
```

### 2. AggregateActor.cs (lines 346-360) - LoadStateInternalAsync
**Before:**
```csharp
var projectedResult = concreteAggregate.Project(deltaEvents, _partitionInfo.Projector);
// ... error handling ...
_currentAggregate = projectedResult.GetValue();
_hasUnsavedChanges = true;
```

**After:**
```csharp
// Only project and mark as changed if there are delta events
if (deltaEvents.Count > 0)
{
    var projectedResult = concreteAggregate.Project(deltaEvents, _partitionInfo.Projector);
    // ... error handling ...
    _currentAggregate = projectedResult.GetValue();
    _hasUnsavedChanges = true;
}
else
{
    _currentAggregate = concreteAggregate;
}
```

### 3. ProtobufAggregateActor.cs - Same fixes applied

## Files Modified
1. `/src/Sekiban.Pure.Dapr/Actors/AggregateActor.cs`
2. `/src/Sekiban.Pure.Dapr/Actors/ProtobufAggregateActor.cs`

## Testing
Created test cases to verify the fix:
- `EventOrNone_None_Should_Not_Increment_Version` - Verifies version doesn't increment when no event is produced
- `EventOrNone_WithEvent_Should_Increment_Version` - Verifies version increments when an event is produced
- `Multiple_EventOrNone_None_Should_Not_Change_Version` - Verifies multiple no-op commands don't affect version

All tests are passing after the fix.

## Impact
This fix ensures that:
1. Aggregate versions only increment when actual state changes occur
2. Unnecessary state persistence is avoided when no events are produced
3. Version tracking accurately reflects the number of actual state changes
4. Performance is improved by avoiding unnecessary saves

## Additional Notes
The core event sourcing logic was already correct - the issue was only in the Dapr actor's state management layer. The fix ensures that the actor only persists state when actual changes have occurred.