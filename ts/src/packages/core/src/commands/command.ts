import type { Result } from 'neverthrow';
import type { IEventPayload } from '../events/event-payload.js';
import type { Aggregate } from '../aggregates/aggregate.js';
import type { PartitionKeys } from '../partition-keys/partition-keys.js';
import type { CommandValidationError, SekibanError } from '../errors/sekiban-error.js';
import type { ITypedAggregatePayload, EmptyAggregatePayload } from '../aggregates/aggregate-projector.js';

/**
 * Command that can be applied to any payload type in the union
 */
export interface ICommand<TPayloadUnion extends ITypedAggregatePayload> {
  readonly commandType: string;
  
  /**
   * Specify partition keys for the command
   */
  specifyPartitionKeys(): PartitionKeys;
  
  /**
   * Validate the command before execution
   */
  validate(): Result<void, CommandValidationError>;
  
  /**
   * Handle the command and return events
   */
  handle(
    aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>
  ): Result<IEventPayload[], SekibanError>;
}

/**
 * Command that can only be applied to a specific payload type
 */
export interface IConstrainedPayloadCommand<
  TPayloadUnion extends ITypedAggregatePayload,
  TRequiredPayload extends TPayloadUnion
> extends ICommand<TPayloadUnion> {
  
  /**
   * Get the required payload type for this command
   */
  getRequiredPayloadType(): string;
  
  /**
   * Handle the command with type-safe payload
   */
  handleTyped(
    aggregate: Aggregate<TRequiredPayload>
  ): Result<IEventPayload[], SekibanError>;
}

/**
 * Command that can only be applied to empty aggregates (creation commands)
 */
export interface ICreationCommand<TPayloadUnion extends ITypedAggregatePayload> 
  extends ICommand<TPayloadUnion> {
  
  /**
   * Handle creation command
   */
  handleCreation(
    aggregate: Aggregate<EmptyAggregatePayload>
  ): Result<IEventPayload[], SekibanError>;
}

/**
 * Abstract base class for creation commands
 */
export abstract class CreationCommand<TPayloadUnion extends ITypedAggregatePayload> 
  implements ICreationCommand<TPayloadUnion> {
  
  abstract readonly commandType: string;
  
  abstract specifyPartitionKeys(): PartitionKeys;
  abstract validate(): Result<void, CommandValidationError>;
  abstract handleCreation(aggregate: Aggregate<EmptyAggregatePayload>): Result<IEventPayload[], SekibanError>;
  
  handle(aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>): Result<IEventPayload[], SekibanError> {
    if (aggregate.payload.aggregateType !== 'Empty') {
      return {
        isOk: () => false,
        isErr: () => true,
        error: {
          type: 'DomainError',
          message: 'Creation command can only be applied to empty aggregates',
          details: { currentType: aggregate.payload.aggregateType }
        }
      } as Result<IEventPayload[], SekibanError>;
    }
    
    return this.handleCreation(aggregate as Aggregate<EmptyAggregatePayload>);
  }
}

/**
 * Abstract base class for constrained payload commands
 */
export abstract class ConstrainedPayloadCommand<
  TPayloadUnion extends ITypedAggregatePayload,
  TRequiredPayload extends TPayloadUnion
> implements IConstrainedPayloadCommand<TPayloadUnion, TRequiredPayload> {
  
  abstract readonly commandType: string;
  
  abstract specifyPartitionKeys(): PartitionKeys;
  abstract validate(): Result<void, CommandValidationError>;
  abstract getRequiredPayloadType(): string;
  abstract handleTyped(aggregate: Aggregate<TRequiredPayload>): Result<IEventPayload[], SekibanError>;
  
  handle(aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>): Result<IEventPayload[], SekibanError> {
    const requiredType = this.getRequiredPayloadType();
    
    if (aggregate.payload.aggregateType !== requiredType) {
      return {
        isOk: () => false,
        isErr: () => true,
        error: {
          type: 'DomainError',
          message: `Command requires payload type '${requiredType}' but found '${aggregate.payload.aggregateType}'`,
          details: { 
            required: requiredType, 
            found: aggregate.payload.aggregateType,
            commandType: this.commandType
          }
        }
      } as Result<IEventPayload[], SekibanError>;
    }
    
    return this.handleTyped(aggregate as Aggregate<TRequiredPayload>);
  }
}