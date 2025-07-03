import { Result, ResultAsync, ok, err, okAsync, errAsync } from 'neverthrow'
import { IEventStorageProvider, StorageProviderConfig, EventBatch, SnapshotData, StorageError, ConcurrencyError } from './storage-provider'
import { IEvent } from '../events/event'
import { PartitionKeys } from '../documents/partition-keys'

/**
 * In-memory storage provider for testing and development
 */
export class InMemoryStorageProvider implements IEventStorageProvider {
  private events = new Map<string, IEvent[]>()
  private snapshots = new Map<string, SnapshotData[]>()
  private versions = new Map<string, number>()

  constructor(private config: StorageProviderConfig) {}

  saveEvents(batch: EventBatch): ResultAsync<void, StorageError> {
    return ResultAsync.fromPromise(
      this.doSaveEvents(batch),
      (e) => {
        if (e instanceof StorageError) {
          return e
        }
        return new StorageError('Failed to save events', 'SAVE_FAILED', e as Error)
      }
    )
  }

  private async doSaveEvents(batch: EventBatch): Promise<void> {
    const key = batch.partitionKeys.toString()
    const currentVersion = this.versions.get(key) || 0

    // Check version for optimistic concurrency
    if (batch.expectedVersion !== currentVersion) {
      throw new ConcurrencyError(
        `Version mismatch: expected ${batch.expectedVersion} but was ${currentVersion}`,
        batch.expectedVersion,
        currentVersion
      )
    }

    // Get or create event list
    const eventList = this.events.get(key) || []
    
    // Add new events
    eventList.push(...batch.events)
    this.events.set(key, eventList)

    // Update version
    const newVersion = Math.max(currentVersion, ...batch.events.map(e => e.version))
    this.versions.set(key, newVersion)
  }

  loadEventsByPartitionKey(partitionKeys: PartitionKeys): ResultAsync<IEvent[], StorageError> {
    return ResultAsync.fromSafePromise(
      this.doLoadEventsByPartitionKey(partitionKeys)
    ).mapErr(e => new StorageError('Failed to load events', 'LOAD_FAILED', e as Error))
  }

  private async doLoadEventsByPartitionKey(partitionKeys: PartitionKeys): Promise<IEvent[]> {
    const key = partitionKeys.toString()
    return this.events.get(key) || []
  }

  loadEvents(partitionKeys: PartitionKeys, afterEventId?: string): ResultAsync<IEvent[], StorageError> {
    return ResultAsync.fromSafePromise(
      this.doLoadEvents(partitionKeys, afterEventId)
    ).mapErr(e => new StorageError('Failed to load events', 'LOAD_FAILED', e as Error))
  }

  private async doLoadEvents(partitionKeys: PartitionKeys, afterEventId?: string): Promise<IEvent[]> {
    const key = partitionKeys.toString()
    const events = this.events.get(key) || []
    
    if (!afterEventId) {
      return events
    }

    // Find the index of the event after which to start
    const index = events.findIndex(e => e.id === afterEventId)
    if (index === -1) {
      return events // If not found, return all events
    }

    // Return events after the specified ID
    return events.slice(index + 1)
  }

  getLatestSnapshot(partitionKeys: PartitionKeys): ResultAsync<SnapshotData | null, StorageError> {
    return ResultAsync.fromSafePromise(
      this.doGetLatestSnapshot(partitionKeys)
    ).mapErr(e => new StorageError('Failed to get snapshot', 'SNAPSHOT_FAILED', e as Error))
  }

  private async doGetLatestSnapshot(partitionKeys: PartitionKeys): Promise<SnapshotData | null> {
    const key = partitionKeys.toString()
    const snapshots = this.snapshots.get(key) || []
    
    if (snapshots.length === 0) {
      return null
    }

    // Return the latest snapshot (highest version)
    return snapshots.reduce((latest, current) => 
      current.version > latest.version ? current : latest
    )
  }

  saveSnapshot(snapshot: SnapshotData): ResultAsync<void, StorageError> {
    return ResultAsync.fromSafePromise(
      this.doSaveSnapshot(snapshot)
    ).mapErr(e => new StorageError('Failed to save snapshot', 'SNAPSHOT_FAILED', e as Error))
  }

  private async doSaveSnapshot(snapshot: SnapshotData): Promise<void> {
    const key = snapshot.partitionKeys.toString()
    const snapshots = this.snapshots.get(key) || []
    
    // Add new snapshot
    snapshots.push({
      ...snapshot,
      createdAt: new Date() // Ensure createdAt is set
    })
    
    this.snapshots.set(key, snapshots)
  }

  initialize(): ResultAsync<void, StorageError> {
    // Nothing to initialize for in-memory storage
    return okAsync(undefined)
  }

  close(): ResultAsync<void, StorageError> {
    // Clear all data
    this.events.clear()
    this.snapshots.clear()
    this.versions.clear()
    return okAsync(undefined)
  }
}