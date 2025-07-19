export { PostgresEventStore } from './postgres-event-store';
export { createPostgresEventStore } from './postgres-storage-provider';

// Auto-register when imported
import './postgres-storage-provider';