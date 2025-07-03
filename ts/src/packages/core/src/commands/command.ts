import { Result } from 'neverthrow'
import { PartitionKeys } from '../documents/partition-keys'
import { IAggregatePayload } from '../aggregates/aggregate-payload'
import { IAggregate } from '../aggregates/aggregate'
import { IProjector } from '../aggregates/projector-interface'
import { IEventPayload } from '../events/event-payload'
import { IEvent } from '../events/event'
import { EventOrNone } from '../aggregates/projector-interface'
import { ValidationError } from '../result/errors'

/**
 * Marker interface for commands
 */
export interface ICommand {}

/**
 * Command handler interface
 */
export interface ICommandHandler<TCommand extends ICommand, TAggregatePayload extends IAggregatePayload> {
  /**
   * Handle the command and produce events
   */
  handle(
    command: TCommand,
    context: ICommandContext<TAggregatePayload>
  ): Result<EventOrNone, Error>
}

/**
 * Command context with access to aggregate state
 */
export interface ICommandContext<TAggregatePayload extends IAggregatePayload> {
  /**
   * Get the current aggregate
   */
  getAggregate(): IAggregate<TAggregatePayload>
  
  /**
   * Create an event with proper metadata
   */
  createEvent(payload: IEventPayload): IEvent
}

/**
 * Command context without aggregate state (for new aggregates)
 */
export interface ICommandContextWithoutState {
  /**
   * Create an event with proper metadata
   */
  createEvent(payload: IEventPayload): IEvent
}

/**
 * Combined command and handler interface
 */
export interface ICommandWithHandler<
  TCommand extends ICommand,
  TProjector extends IProjector<any>
> extends ICommand {
  /**
   * Validate the command
   */
  validate(): Result<void, ValidationError[]>
  
  /**
   * Get partition keys for this command
   */
  getPartitionKeys(): PartitionKeys
  
  /**
   * Handle the command
   */
  handle(
    command: TCommand,
    context: ICommandContextWithoutState | ICommandContext<any>
  ): Result<EventOrNone, Error>
}

/**
 * Command response
 */
export interface CommandResponse {
  /**
   * Whether the command succeeded
   */
  success: boolean
  
  /**
   * The aggregate ID (if successful)
   */
  aggregateId?: string
  
  /**
   * The new version (if successful)
   */
  version?: number
  
  /**
   * The event ID (if successful)
   */
  eventId?: string
  
  /**
   * Error message (if failed)
   */
  error?: string
}

/**
 * Create a command response
 */
export function createCommandResponse(options: {
  success: boolean
  aggregateId?: string
  version?: number
  eventId?: string
  error?: string
}): CommandResponse {
  return {
    success: options.success,
    aggregateId: options.aggregateId,
    version: options.version,
    eventId: options.eventId,
    error: options.error
  }
}