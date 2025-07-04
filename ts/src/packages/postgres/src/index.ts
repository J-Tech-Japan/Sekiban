export { PostgresEventStore } from './postgres-event-store.js';
export { createPostgresEventStore } from './postgres-storage-provider.js';

// Auto-register when imported
import './postgres-storage-provider.js';