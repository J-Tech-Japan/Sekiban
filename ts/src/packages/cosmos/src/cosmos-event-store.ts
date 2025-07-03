import { Database, Container, CosmosClient, PartitionKeyDefinition } from '@azure/cosmos'
import { ResultAsync, okAsync, errAsync } from 'neverthrow'
import {
  IEventStorageProvider,
  EventBatch,
  SnapshotData,
  StorageError,
  ConnectionError,
  ConcurrencyError,
  IEvent,
  PartitionKeys
} from '@sekiban/core'

/**
 * CosmosDB event store implementation
 */
export class CosmosEventStore implements IEventStorageProvider {
  private eventsContainer?: Container
  private snapshotsContainer?: Container

  constructor(private database: Database) {}

  /**
   * Initialize the database containers
   */
  initialize(): ResultAsync<void, StorageError> {
    return ResultAsync.fromPromise(
      (async () => {
        // Create events container with aggregateId as partition key
        const { container: events } = await this.database.containers.createIfNotExists({
          id: 'events',
          partitionKey: {
            paths: ['/aggregateId'],
            kind: 'Hash'
          } as PartitionKeyDefinition,
          indexingPolicy: {
            indexingMode: 'consistent',
            automatic: true,
            includedPaths: [{ path: '/*' }],
            excludedPaths: [{ path: '/"_etag"/?' }]
          }
        })
        this.eventsContainer = events

        // Create snapshots container
        const { container: snapshots } = await this.database.containers.createIfNotExists({
          id: 'snapshots',
          partitionKey: {
            paths: ['/aggregateId'],
            kind: 'Hash'
          } as PartitionKeyDefinition
        })
        this.snapshotsContainer = snapshots
      })(),
      (error) => this.toStorageError(error, 'INITIALIZATION_FAILED', 'Failed to initialize CosmosDB')
    )
  }

  /**
   * Save events to the database
   */
  saveEvents(batch: EventBatch): ResultAsync<void, StorageError> {
    if (!this.eventsContainer) {
      return errAsync(new ConnectionError('Event store not initialized'))
    }

    if (batch.events.length === 0) {
      return okAsync(undefined)
    }

    return ResultAsync.fromPromise(
      (async () => {
        const container = this.eventsContainer!

        // Check current version
        const query = {
          query: 'SELECT VALUE MAX(c.seq) FROM c WHERE c.aggregateId = @aggregateId',
          parameters: [{ name: '@aggregateId', value: batch.partitionKeys.aggregateId }]
        }
        const { resources } = await container.items.query(query).fetchAll()
        const currentVersion = resources[0] || 0

        if (currentVersion !== batch.expectedVersion) {
          throw new ConcurrencyError(
            `Expected version ${batch.expectedVersion} but current version is ${currentVersion}`,
            batch.expectedVersion,
            currentVersion
          )
        }

        // Use TransactionalBatch for multiple events
        if (batch.events.length === 1) {
          // Single event - direct create
          const event = batch.events[0]
          const document = this.eventToDocument(event, currentVersion + 1, batch.partitionKeys)
          await container.items.create(document)
        } else {
          // Multiple events - use batch
          const batchOps = container.items.batch(batch.partitionKeys.aggregateId)
          
          batch.events.forEach((event, index) => {
            const seq = currentVersion + index + 1
            const document = this.eventToDocument(event, seq, batch.partitionKeys)
            batchOps.create(document)
          })

          const response = await batchOps.execute()
          
          if (!response.result) {
            throw new StorageError('Batch operation failed', 'BATCH_FAILED')
          }
        }
      })(),
      (error) => {
        if (error instanceof ConcurrencyError) {
          return error
        }
        return this.toStorageError(error, 'SAVE_FAILED', 'Failed to save events')
      }
    )
  }

  /**
   * Load all events for a partition key
   */
  loadEventsByPartitionKey(partitionKeys: PartitionKeys): ResultAsync<IEvent[], StorageError> {
    if (!this.eventsContainer) {
      return errAsync(new ConnectionError('Event store not initialized'))
    }

    return ResultAsync.fromPromise(
      (async () => {
        const query = {
          query: 'SELECT * FROM c WHERE c.aggregateId = @aggregateId ORDER BY c.seq',
          parameters: [{ name: '@aggregateId', value: partitionKeys.aggregateId }]
        }
        
        const { resources } = await this.eventsContainer!.items
          .query(query, { partitionKey: partitionKeys.aggregateId })
          .fetchAll()

        return resources.map(doc => this.documentToEvent(doc))
      })(),
      (error) => this.toStorageError(error, 'LOAD_FAILED', 'Failed to load events')
    )
  }

  /**
   * Load events starting after a specific event ID
   */
  loadEvents(partitionKeys: PartitionKeys, afterEventId?: string): ResultAsync<IEvent[], StorageError> {
    if (!this.eventsContainer) {
      return errAsync(new ConnectionError('Event store not initialized'))
    }

    if (!afterEventId) {
      return this.loadEventsByPartitionKey(partitionKeys)
    }

    return ResultAsync.fromPromise(
      (async () => {
        const query = {
          query: 'SELECT * FROM c WHERE c.aggregateId = @aggregateId AND c.sortableUniqueId > @afterEventId ORDER BY c.seq',
          parameters: [
            { name: '@aggregateId', value: partitionKeys.aggregateId },
            { name: '@afterEventId', value: afterEventId }
          ]
        }
        
        const { resources } = await this.eventsContainer!.items
          .query(query, { partitionKey: partitionKeys.aggregateId })
          .fetchAll()

        return resources.map(doc => this.documentToEvent(doc))
      })(),
      (error) => this.toStorageError(error, 'LOAD_FAILED', 'Failed to load events')
    )
  }

  /**
   * Get the latest snapshot for an aggregate
   */
  getLatestSnapshot(partitionKeys: PartitionKeys): ResultAsync<SnapshotData | null, StorageError> {
    if (!this.snapshotsContainer) {
      return errAsync(new ConnectionError('Event store not initialized'))
    }

    return ResultAsync.fromPromise(
      (async () => {
        try {
          const { resource } = await this.snapshotsContainer!.item(
            partitionKeys.aggregateId,
            partitionKeys.aggregateId
          ).read()

          if (!resource) {
            return null
          }

          return {
            partitionKeys,
            version: resource.version,
            aggregateType: resource.aggregateType,
            payload: resource.payload,
            createdAt: new Date(resource.createdAt),
            lastEventId: resource.lastEventId
          }
        } catch (error: any) {
          if (error.code === 404) {
            return null
          }
          throw error
        }
      })(),
      (error) => this.toStorageError(error, 'LOAD_FAILED', 'Failed to load snapshot')
    )
  }

  /**
   * Save a snapshot
   */
  saveSnapshot(snapshot: SnapshotData): ResultAsync<void, StorageError> {
    if (!this.snapshotsContainer) {
      return errAsync(new ConnectionError('Event store not initialized'))
    }

    return ResultAsync.fromPromise(
      (async () => {
        const document = {
          id: snapshot.partitionKeys.aggregateId,
          aggregateId: snapshot.partitionKeys.aggregateId,
          version: snapshot.version,
          aggregateType: snapshot.aggregateType,
          payload: snapshot.payload,
          createdAt: snapshot.createdAt.toISOString(),
          lastEventId: snapshot.lastEventId
        }

        await this.snapshotsContainer!.items.upsert(document)
      })(),
      (error) => this.toStorageError(error, 'SAVE_FAILED', 'Failed to save snapshot')
    )
  }

  /**
   * Close the connection (no-op for CosmosDB)
   */
  close(): ResultAsync<void, StorageError> {
    return okAsync(undefined)
  }

  /**
   * Convert an event to a CosmosDB document
   */
  private eventToDocument(event: IEvent, seq: number, partitionKeys: PartitionKeys): any {
    return {
      id: `${partitionKeys.aggregateId}_${seq}`,
      aggregateId: partitionKeys.aggregateId,
      aggregateType: partitionKeys.aggregate,
      seq,
      eventType: event.eventType,
      payload: event.payload,
      sortableUniqueId: event.sortableUniqueId,
      meta: {
        partitionKeys: event.partitionKeys,
        version: event.version
      },
      ts: new Date().toISOString()
    }
  }

  /**
   * Convert a CosmosDB document to an event
   */
  private documentToEvent(doc: any): IEvent {
    return {
      sortableUniqueId: doc.sortableUniqueId,
      eventType: doc.eventType,
      payload: doc.payload,
      aggregateId: doc.aggregateId,
      partitionKeys: doc.meta.partitionKeys,
      version: doc.meta.version
    }
  }

  /**
   * Convert an error to a StorageError
   */
  private toStorageError(error: unknown, code: string, message: string): StorageError {
    if (error instanceof StorageError) {
      return error
    }
    return new StorageError(
      `${message}: ${error instanceof Error ? error.message : 'Unknown error'}`,
      code,
      error instanceof Error ? error : undefined
    )
  }
}