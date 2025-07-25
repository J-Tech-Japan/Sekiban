import { Result } from 'neverthrow'
import { IEvent } from '../events/event'
import { SekibanError } from '../result/errors'

/**
 * Common interface for multi-projectors
 */
export interface IMultiProjectorCommon {
  /**
   * Get the version of this projector
   */
  getVersion(): string
}

/**
 * Interface for multi-projectors that can project events across multiple aggregates
 */
export interface IMultiProjector<TMultiAggregatePayload> extends IMultiProjectorCommon {
  /**
   * Project an event onto the current state
   */
  project(payload: TMultiAggregatePayload, event: IEvent): Result<TMultiAggregatePayload, SekibanError>
  
  /**
   * Generate the initial payload state
   */
  generateInitialPayload(): TMultiAggregatePayload
  
  /**
   * Get the name of this multi-projector
   */
  getMultiProjectorName(): string
}

/**
 * Abstract base class for multi-projectors
 */
export abstract class MultiProjector<TMultiAggregatePayload> implements IMultiProjector<TMultiAggregatePayload> {
  getVersion(): string {
    return 'initial'
  }
  
  abstract project(payload: TMultiAggregatePayload, event: IEvent): Result<TMultiAggregatePayload, SekibanError>
  abstract generateInitialPayload(): TMultiAggregatePayload
  abstract getMultiProjectorName(): string
}