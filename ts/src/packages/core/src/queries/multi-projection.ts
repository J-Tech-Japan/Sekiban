import { IEventPayload } from '../events/event-payload.js'

/**
 * Interface for multi-projectors that handle events from multiple aggregates
 */
export interface IMultiProjector<TPayload> {
  /**
   * Get the type name of this projector
   */
  getTypeName(): string
  
  /**
   * Get the version of this projector
   */
  getVersion(): number
  
  /**
   * Get the initial state
   */
  getInitialState(): TPayload
  
  /**
   * Project an event onto the current state
   */
  project(state: TPayload, event: IEventPayload): TPayload
}

/**
 * State container for multi-projections
 */
export class MultiProjectionState<TProjector extends IMultiProjector<any>> {
  constructor(
    public readonly projector: TProjector,
    public readonly payload: ReturnType<TProjector['getInitialState']>,
    public readonly version: number
  ) {}
  
  /**
   * Apply an event to create a new state
   */
  applyEvent(event: IEventPayload): MultiProjectionState<TProjector> {
    const newPayload = this.projector.project(this.payload, event)
    return new MultiProjectionState(
      this.projector,
      newPayload,
      this.version + 1
    )
  }
}