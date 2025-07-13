// Export the new storage contract test suite
export { defineStorageContractTests } from './storage-contract-new'

// Re-export useful testing utilities from core
export {
  PartitionKeys,
  SortableUniqueId,
  IEvent,
  IEventStore,
  EventRetrievalInfo,
  createEvent,
  createEventMetadata,
  type IEventPayload
} from '@sekiban/core'

// Legacy exports - will be removed in future versions
export { StorageContractTestSuite } from './storage-contract'
export { defineStorageContractTests as defineStorageContractTestsV2 } from './storage-contract-v2'