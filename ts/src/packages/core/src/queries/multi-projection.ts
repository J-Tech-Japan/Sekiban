import { IEventPayload } from '../events/event-payload'

/**
 * Interface for simple multi-projectors that handle events from multiple aggregates
 * @deprecated Use IMultiProjector from '../projectors/multi-projector' instead
 */
export interface ISimpleMultiProjector<TPayload> {
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
 * State container for simple multi-projections
 * @deprecated Use MultiProjectionState from '../projectors/multi-projector-types' instead
 */
export class SimpleMultiProjectionState<TProjector extends ISimpleMultiProjector<any>> {
  constructor(
    public readonly projector: TProjector,
    public readonly payload: ReturnType<TProjector['getInitialState']>,
    public readonly version: number
  ) {}
  
  /**
   * Apply an event to create a new state
   */
  applyEvent(event: IEventPayload): SimpleMultiProjectionState<TProjector> {
    const newPayload = this.projector.project(this.payload, event)
    return new SimpleMultiProjectionState(
      this.projector,
      newPayload,
      this.version + 1
    )
  }
}