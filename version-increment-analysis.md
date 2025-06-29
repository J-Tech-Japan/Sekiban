# Analysis: Version Increment Issue with EventOrNone.None

## Problem Description
When a command handler returns `EventOrNone.None` (indicating no state change), the aggregate version is still being incremented, which is incorrect behavior.

## Root Causes Identified

### 1. Unconditional State Marking in AggregateActor
In `/src/Sekiban.Pure.Dapr/Actors/AggregateActor.cs`, line 218:
```csharp
_hasUnsavedChanges = true;
```
This flag is set to `true` after EVERY command execution, regardless of whether any events were produced.

### 2. LoadStateInternalAsync Always Marks State as Changed
In the same file, lines 346:
```csharp
_hasUnsavedChanges = true;
```
When loading state and projecting delta events (even if the delta events list is empty), the state is marked as changed.

### 3. Potential State Persistence Issue
The actor's periodic timer (`SaveStateCallbackAsync`) will save the state whenever `_hasUnsavedChanges` is true, which could lead to unnecessary state persistence and potential version tracking issues.

## Code Flow Analysis

1. **Command Execution Flow:**
   - Command returns `EventOrNone.None`
   - No events are generated
   - `DaprRepository.Save()` correctly returns early with empty list
   - `CommandExecutor` creates response with current version (no increment)
   - BUT: `AggregateActor` sets `_hasUnsavedChanges = true` unconditionally

2. **State Loading Flow:**
   - State is loaded from persistence
   - Delta events are fetched (may be empty)
   - Even with empty delta events, `_hasUnsavedChanges = true` is set

## Recommended Fixes

### Fix 1: Conditional State Marking in ExecuteCommandAsync
```csharp
// Line 217-219 in AggregateActor.cs should be:
_currentAggregate = repository.GetProjectedAggregate(result.Events).UnwrapBox();
if (result.Events.Count > 0)
{
    _hasUnsavedChanges = true;
}
```

### Fix 2: Conditional State Marking in LoadStateInternalAsync
```csharp
// Around line 339-346, only mark as changed if delta events exist:
if (deltaEvents.Count > 0)
{
    var projectedResult = concreteAggregate.Project(deltaEvents, _partitionInfo.Projector);
    if (!projectedResult.IsSuccess)
    {
        throw new InvalidOperationException(
            $"Failed to project delta events: {projectedResult.GetException().Message}");
    }
    _currentAggregate = projectedResult.GetValue();
    _hasUnsavedChanges = true;
}
else
{
    _currentAggregate = concreteAggregate;
}
```

### Fix 3: Ensure Version is Not Incremented Without Events
The version tracking appears to be correctly implemented in the core logic:
- `Aggregate.Project()` only increments version when processing actual events
- `CommandExecutor` correctly uses current version when no events are produced

The issue is primarily with the unnecessary state persistence triggered by the unconditional `_hasUnsavedChanges = true`.

## Testing Recommendation
Create integration tests that:
1. Execute a command that returns `EventOrNone.None`
2. Verify the aggregate version remains unchanged
3. Reload the aggregate and verify version is still unchanged
4. Execute another command and verify version increments correctly

## Impact
This issue could cause:
- Unnecessary state persistence operations
- Confusion about aggregate version numbers
- Potential performance impact due to unnecessary saves
- Incorrect version tracking in audit logs or event sourcing projections