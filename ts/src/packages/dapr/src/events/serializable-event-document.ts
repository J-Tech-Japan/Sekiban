import { IEvent, IEventPayload, PartitionKeys, SortableUniqueId } from '@sekiban/core';
import { gzip, ungzip } from 'node:zlib';
import { promisify } from 'node:util';

const gzipAsync = promisify(gzip);
const ungzipAsync = promisify(ungzip);

/**
 * Serializable event document format for Dapr pub/sub
 * Matches the C# SerializableEventDocument structure
 */
export interface SerializableEventDocument {
  // Event ID
  Id: string;
  SortableUniqueId: string;
  Version: number;
  
  // Partition keys (flattened)
  AggregateId: string;
  AggregateGroup: string;
  RootPartitionKey: string;
  
  // Event info
  PayloadTypeName: string;  // This is the event type name (e.g., "TaskCreated")
  TimeStamp: string;  // ISO string
  PartitionKey: string;
  
  // Metadata (flattened)
  CausationId: string;
  CorrelationId: string;
  ExecutedUser: string;
  
  // Payload (compressed as base64 string for JSON transport)
  CompressedPayloadJson: string;  // Base64 encoded compressed JSON
  
  // Version info
  PayloadAssemblyVersion: string;
}

/**
 * Convert an IEvent to SerializableEventDocument
 */
export async function eventToSerializableDocument(event: IEvent): Promise<SerializableEventDocument> {
  // Serialize payload to JSON
  const payloadJson = JSON.stringify(event.payload);
  const payloadBuffer = Buffer.from(payloadJson, 'utf-8');
  
  // Compress the payload
  const compressed = await gzipAsync(payloadBuffer);
  const compressedBase64 = compressed.toString('base64');
  
  return {
    Id: event.id.value || event.id.toString(),
    SortableUniqueId: event.sortableUniqueId?.value || event.sortableUniqueId?.toString() || event.id.toString(),
    Version: event.version,
    
    // Partition keys
    AggregateId: event.aggregateId || event.partitionKeys.aggregateId,
    AggregateGroup: event.aggregateGroup || event.partitionKeys.group || 'default',
    RootPartitionKey: event.partitionKeys.rootPartitionKey || 'default',
    
    // Event info - PayloadTypeName is the event type!
    PayloadTypeName: event.eventType,
    TimeStamp: (event.timestamp instanceof Date ? event.timestamp : new Date(event.timestamp as any)).toISOString(),
    PartitionKey: event.partitionKey || event.partitionKeys.partitionKey || '',
    
    // Metadata
    CausationId: event.metadata?.causationId || '',
    CorrelationId: event.metadata?.correlationId || '',
    ExecutedUser: event.metadata?.executedUser || event.metadata?.userId || '',
    
    // Compressed payload
    CompressedPayloadJson: compressedBase64,
    
    // Version
    PayloadAssemblyVersion: '0.0.0.0'
  };
}

/**
 * Convert SerializableEventDocument back to IEvent format
 */
export async function serializableDocumentToEvent(doc: SerializableEventDocument): Promise<IEvent> {
  // Decompress payload
  const compressedBuffer = Buffer.from(doc.CompressedPayloadJson, 'base64');
  const decompressed = await ungzipAsync(compressedBuffer);
  const payloadJson = decompressed.toString('utf-8');
  const payload = JSON.parse(payloadJson);
  
  // Reconstruct partition keys
  const partitionKeys = new PartitionKeys(
    doc.AggregateId,
    doc.AggregateGroup,
    doc.RootPartitionKey
  );
  
  // Create sortable ID
  const sortableId = SortableUniqueId.fromString(doc.SortableUniqueId).unwrapOr(SortableUniqueId.create());
  
  return {
    id: sortableId,
    aggregateType: doc.AggregateGroup || 'unknown',
    aggregateId: doc.AggregateId,
    eventType: doc.PayloadTypeName,  // PayloadTypeName is the event type!
    payload: payload,
    version: doc.Version,
    partitionKeys: partitionKeys,
    sortableUniqueId: sortableId,
    timestamp: new Date(doc.TimeStamp),
    metadata: {
      causationId: doc.CausationId,
      correlationId: doc.CorrelationId,
      userId: doc.ExecutedUser,
      executedUser: doc.ExecutedUser,
      timestamp: new Date(doc.TimeStamp)
    },
    // Additional fields
    partitionKey: doc.PartitionKey,
    aggregateGroup: doc.AggregateGroup,
    eventData: payload
  };
}