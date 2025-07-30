export { CosmosEventStore } from './cosmos-event-store';
export { createCosmosEventStore, type CosmosStorageProviderConfig } from './cosmos-storage-provider';

// Auto-register when imported
import './cosmos-storage-provider';