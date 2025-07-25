import { Result } from 'neverthrow'
import { IMultiProjector, IMultiProjectorCommon } from './multi-projector'
import { IEvent } from '../events/event'
import { SekibanError } from '../result/errors'

/**
 * State for multi-projections
 */
export interface IMultiProjectorStateCommon {
  projectorCommon: IMultiProjectorCommon
  lastEventId: string
  lastSortableUniqueId: string
  version: number
  appliedSnapshotVersion: number
  rootPartitionKey: string
}

/**
 * Typed multi-projection state
 */
export class MultiProjectionState<TMultiProjector extends IMultiProjector<any>> 
  implements IMultiProjectorStateCommon {
  
  constructor(
    public readonly payload: ReturnType<TMultiProjector['generateInitialPayload']>,
    public readonly lastEventId: string,
    public readonly lastSortableUniqueId: string,
    public readonly version: number,
    public readonly appliedSnapshotVersion: number,
    public readonly rootPartitionKey: string,
    public readonly projector: TMultiProjector
  ) {}
  
  get projectorCommon(): IMultiProjectorCommon {
    return this.projector
  }
  
  /**
   * Apply an event to create a new state
   */
  applyEvent(event: IEvent): Result<MultiProjectionState<TMultiProjector>, SekibanError> {
    return this.projector.project(this.payload, event).map(newPayload => 
      new MultiProjectionState(
        newPayload,
        event.id.value,
        event.sortableUniqueId.value,
        this.version + 1,
        this.appliedSnapshotVersion,
        this.rootPartitionKey,
        this.projector
      )
    )
  }
  
  /**
   * Get the payload version identifier
   */
  getPayloadVersionIdentifier(): string {
    return this.projector.getVersion()
  }
}

/**
 * Interface for multi-projector types management
 */
export interface IMultiProjectorTypes {
  /**
   * Project a single event
   */
  project(multiProjector: IMultiProjectorCommon, event: IEvent): Result<IMultiProjectorCommon, SekibanError>
  
  /**
   * Project multiple events
   */
  projectEvents(multiProjector: IMultiProjectorCommon, events: readonly IEvent[]): Result<IMultiProjectorCommon, SekibanError>
  
  /**
   * Get projector from name
   */
  getProjectorFromMultiProjectorName(grainName: string): IMultiProjectorCommon | undefined
  
  /**
   * Get name from projector
   */
  getMultiProjectorNameFromMultiProjector(multiProjector: IMultiProjectorCommon): Result<string, SekibanError>
  
  /**
   * Convert to typed state
   */
  toTypedState(state: IMultiProjectorStateCommon): IMultiProjectorStateCommon
  
  /**
   * Get all multi-projector types
   */
  getMultiProjectorTypes(): string[]
  
  /**
   * Generate initial payload for a projector type
   */
  generateInitialPayload(projectorType: string): Result<IMultiProjectorCommon, SekibanError>
  
  /**
   * Serialize a multi-projector
   */
  serializeMultiProjector(multiProjector: IMultiProjectorCommon): Promise<Result<string, SekibanError>>
  
  /**
   * Deserialize a multi-projector
   */
  deserializeMultiProjector(json: string, typeFullName: string): Promise<Result<IMultiProjectorCommon, SekibanError>>
}