import { IAggregatePayload } from './aggregate-payload'
import { IEventPayload } from '../events/event-payload'
import { IEvent } from '../events/event'

/**
 * Interface for aggregate projectors - pure functions that apply events to aggregates
 */
export interface IAggregateProjector<TPayload extends IAggregatePayload> {
  /**
   * Get the version of this projector (for schema evolution)
   */
  getVersion(): number
  
  /**
   * Project an event onto the current aggregate state to produce a new state
   * This should be a pure function with no side effects
   */
  project(payload: TPayload, event: IEventPayload): TPayload
}

/**
 * Extended projector interface with type information
 */
export interface IProjector<TPayload extends IAggregatePayload> extends IAggregateProjector<TPayload> {
  /**
   * Get the type name of this projector
   */
  getTypeName(): string
}

/**
 * Result of a projection operation
 */
export class ProjectionResult<TPayload extends IAggregatePayload> {
  private constructor(
    public readonly isSuccess: boolean,
    public readonly payload?: TPayload,
    public readonly error?: string
  ) {}
  
  /**
   * Create a successful projection result
   */
  static success<T extends IAggregatePayload>(payload: T): ProjectionResult<T> {
    return new ProjectionResult(true, payload, undefined)
  }
  
  /**
   * Create a failed projection result
   */
  static error<T extends IAggregatePayload>(error: string): ProjectionResult<T> {
    return new ProjectionResult(false, undefined, error)
  }
}

/**
 * Result of command handling - either events or none
 */
export class EventOrNone {
  private constructor(
    public readonly hasEvent: boolean,
    public readonly event?: IEvent,
    public readonly events?: IEvent[]
  ) {}
  
  /**
   * Create a result with a single event
   */
  static event(event: IEvent): EventOrNone {
    return new EventOrNone(true, event, [event])
  }
  
  /**
   * Create a result with multiple events
   */
  static events(events: IEvent[]): EventOrNone {
    return new EventOrNone(events.length > 0, events[0], events)
  }
  
  /**
   * Create a result with no events
   */
  static none(): EventOrNone {
    return new EventOrNone(false, undefined, undefined)
  }
}

/**
 * Helper function to create a projector from a projection function
 */
export function createProjector<TPayload extends IAggregatePayload>(
  typeName: string,
  version: number,
  projectFn: (payload: TPayload, event: IEventPayload) => TPayload
): IProjector<TPayload> {
  return {
    getTypeName: () => typeName,
    getVersion: () => version,
    project: projectFn
  }
}