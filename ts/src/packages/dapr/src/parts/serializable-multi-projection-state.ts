import { SekibanDomainTypes, IMultiProjectorCommon, OptionalValue } from '@sekiban/core';
import { gunzip, gzip } from 'node:zlib';
import { promisify } from 'node:util';

const gzipAsync = promisify(gzip);
const gunzipAsync = promisify(gunzip);

/**
 * Serializable state for MultiProjector actors that avoids interface serialization issues.
 * This class compresses and stores the multiProjector state as JSON strings rather than
 * attempting to directly serialize interface types.
 */
export interface SerializableMultiProjectionState {
  /**
   * The compressed JSON representation of the IMultiProjectorCommon Payload
   */
  compressedPayloadJson?: Buffer;
  
  /**
   * The full type name of the Payload
   */
  payloadTypeName: string;
  
  /**
   * Version identifier for the Payload type
   */
  payloadVersion: string;
  
  /**
   * Last event ID processed by the projector
   */
  lastEventId: string;
  
  /**
   * Last sortable unique ID processed by the projector
   */
  lastSortableUniqueId: string;
  
  /**
   * Version of the state
   */
  version: number;
  
  /**
   * The version of the snapshot that was applied
   */
  appliedSnapshotVersion: number;
  
  /**
   * The root partition key
   */
  rootPartitionKey: string;
}

export interface MultiProjectionState {
  projectorCommon: IMultiProjectorCommon;
  lastEventId: string;
  lastSortableUniqueId: string;
  version: number;
  appliedSnapshotVersion: number;
  rootPartitionKey: string;
}

/**
 * Creates a SerializableMultiProjectionState from a MultiProjectionState
 */
export async function createSerializableMultiProjectionState(
  state: MultiProjectionState,
  domainTypes: SekibanDomainTypes
): Promise<SerializableMultiProjectionState> {
  const projector = state.projectorCommon;
  
  if (!domainTypes.multiProjectorTypes) {
    throw new Error('MultiProjectorTypes not available');
  }
  
  // Use IMultiProjectorTypes for serialization
  const serializedPayloadResult = await domainTypes.multiProjectorTypes.serializeMultiProjector(projector);
  
  if (serializedPayloadResult.isErr()) {
    throw new Error(`Failed to serialize projector: ${serializedPayloadResult.error}`);
  }
  
  const payloadJson = serializedPayloadResult.value;
  const compressedPayload = await gzipAsync(payloadJson);
  
  const versionString = projector.getVersion();
  
  return {
    compressedPayloadJson: compressedPayload,
    payloadTypeName: projector.constructor.name,
    payloadVersion: versionString,
    lastEventId: state.lastEventId,
    lastSortableUniqueId: state.lastSortableUniqueId,
    version: state.version,
    appliedSnapshotVersion: state.appliedSnapshotVersion,
    rootPartitionKey: state.rootPartitionKey
  };
}

/**
 * Converts a SerializableMultiProjectionState back to a MultiProjectionState
 */
export async function toMultiProjectionState(
  serialized: SerializableMultiProjectionState,
  domainTypes: SekibanDomainTypes
): Promise<MultiProjectionState | null> {
  if (!serialized.compressedPayloadJson) {
    return null;
  }
  
  if (!domainTypes.multiProjectorTypes) {
    throw new Error('MultiProjectorTypes not available');
  }
  
  try {
    // Decompress the payload
    const payloadJson = (await gunzipAsync(serialized.compressedPayloadJson)).toString('utf-8');
    
    // Use IMultiProjectorTypes for deserialization
    const projectorResult = await domainTypes.multiProjectorTypes.deserializeMultiProjector(
      payloadJson,
      serialized.payloadTypeName
    );
    
    if (projectorResult.isErr() || !projectorResult.value) {
      return null;
    }
    
    const projector = projectorResult.value;
    
    if (projector.getVersion() !== serialized.payloadVersion) {
      // Version mismatch
      return null;
    }
    
    // Recreate the MultiProjectionState
    return {
      projectorCommon: projector,
      lastEventId: serialized.lastEventId,
      lastSortableUniqueId: serialized.lastSortableUniqueId,
      version: serialized.version,
      appliedSnapshotVersion: serialized.appliedSnapshotVersion,
      rootPartitionKey: serialized.rootPartitionKey
    };
  } catch (error) {
    // Any exception during deserialization means we can't recover the state
    console.error('Failed to deserialize state:', error);
    return null;
  }
}