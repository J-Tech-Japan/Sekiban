import { z } from 'zod';
import { err, ok, type Result } from 'neverthrow';
import { PartitionKeys, TypedPartitionKeys } from '../documents/index.js';
import type { IEventPayload } from '../events/event-payload.js';
import type { Aggregate } from '../aggregates/aggregate.js';
import type { EmptyAggregatePayload } from '../aggregates/aggregate.js';
import type { SekibanError } from '../result/errors.js';
import { CommandValidationError } from '../result/errors.js';
import type { ITypedAggregatePayload, IAggregateProjector } from '../aggregates/aggregate-projector.js';
import type { IEvent } from '../events/event.js';
import type { Metadata } from '../documents/metadata.js';

/**
 * Command context without aggregate state - base context
 */
export interface ICommandContextWithoutState {
  readonly originalSortableUniqueId: string;
  readonly events: IEvent[];
  readonly partitionKeys: PartitionKeys;
  readonly metadata: Metadata;
  
  getPartitionKeys(): PartitionKeys;
  getNextVersion(): number;
  getCurrentVersion(): number;
  appendEvent(eventPayload: IEventPayload): Result<IEvent, SekibanError>;
  getService<T>(serviceType: new (...args: any[]) => T): Result<T, SekibanError>;
}

/**
 * Command context with aggregate state
 */
export interface ICommandContext<TAggregatePayload extends ITypedAggregatePayload | EmptyAggregatePayload> 
  extends ICommandContextWithoutState {
  getAggregate(): Result<Aggregate<TAggregatePayload>, SekibanError>;
}

/**
 * Command with handler interface aligned with C# design
 */
export interface ICommandWithHandler<
  TCommand,
  TProjector extends IAggregateProjector<TPayloadUnion>,
  TPayloadUnion extends ITypedAggregatePayload,
  TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
> {
  readonly commandType: string;
  
  /**
   * Get the projector for this command
   */
  getProjector(): TProjector;
  
  /**
   * Specify partition keys for the command
   */
  specifyPartitionKeys(command: TCommand): PartitionKeys;
  
  /**
   * Validate the command
   */
  validate(command: TCommand): Result<void, CommandValidationError>;
  
  /**
   * Handle the command and produce events
   */
  handle(
    command: TCommand,
    context: ICommandContext<TAggregatePayload>
  ): Result<IEventPayload[], SekibanError>;
}

/**
 * Command handlers for schema-based commands
 */
export interface CommandHandlers<
  TData,
  TProjector extends IAggregateProjector<TPayloadUnion>,
  TPayloadUnion extends ITypedAggregatePayload,
  TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
> {
  /**
   * Specify partition keys for the command
   */
  specifyPartitionKeys: (data: TData) => PartitionKeys;
  
  /**
   * Perform business validation beyond schema validation
   */
  validate?: (data: TData) => Result<void, CommandValidationError>;
  
  /**
   * Handle the command and return events
   */
  handle: (
    data: TData,
    context: ICommandContext<TAggregatePayload>
  ) => Result<IEventPayload[], SekibanError>;
}

/**
 * Definition structure for a command schema without payload constraint
 */
export interface CommandSchemaDefinition<
  TName extends string,
  TSchema extends z.ZodTypeAny,
  TProjector extends IAggregateProjector<TPayloadUnion>,
  TPayloadUnion extends ITypedAggregatePayload
> {
  type: TName;
  schema: TSchema;
  projector: TProjector;
  handlers: CommandHandlers<z.infer<TSchema>, TProjector, TPayloadUnion>;
}

/**
 * Definition structure for a command schema with payload constraint
 */
export interface CommandSchemaDefinitionWithPayload<
  TName extends string,
  TSchema extends z.ZodTypeAny,
  TProjector extends IAggregateProjector<TPayloadUnion>,
  TPayloadUnion extends ITypedAggregatePayload,
  TAggregatePayload extends TPayloadUnion
> {
  type: TName;
  schema: TSchema;
  projector: TProjector;
  requiredPayloadType: string;
  handlers: CommandHandlers<z.infer<TSchema>, TProjector, TPayloadUnion, TAggregatePayload>;
}

/**
 * Define a command with a Zod schema and handlers aligned with C# design
 * Supports both unconstrained and payload-constrained commands
 * 
 * @param definition - The command definition including type name, schema, projector, and handlers
 * @returns Command definition object that implements ICommandWithHandler
 * 
 * @example
 * ```typescript
 * // Command without payload constraint
 * const CreateUser = defineCommand({
 *   type: 'CreateUser',
 *   schema: z.object({
 *     name: z.string().min(1),
 *     email: z.string().email(),
 *     tenantId: z.string().optional()
 *   }),
 *   projector: new UserProjector(),
 *   handlers: {
 *     specifyPartitionKeys: (data) => data.tenantId 
 *       ? TypedPartitionKeys.Generate(UserProjector, data.tenantId)
 *       : TypedPartitionKeys.Generate(UserProjector),
 *     validate: (data) => {
 *       if (data.email.endsWith('@test.com')) {
 *         return err(new CommandValidationError('CreateUser', ['Test emails not allowed']));
 *       }
 *       return ok(undefined);
 *     },
 *     handle: (data, context) => {
 *       const aggregateId = context.getPartitionKeys().aggregateId;
 *       return ok([UserCreated.create({ 
 *         userId: aggregateId,
 *         name: data.name,
 *         email: data.email
 *       })]);
 *     }
 *   }
 * });
 * 
 * // Command with payload constraint
 * const ActivateUser = defineCommand({
 *   type: 'ActivateUser',
 *   schema: z.object({ 
 *     userId: z.string(),
 *     reason: z.string()
 *   }),
 *   projector: new UserProjector(),
 *   requiredPayloadType: 'InactiveUser',
 *   handlers: {
 *     specifyPartitionKeys: (data) => TypedPartitionKeys.Existing(UserProjector, data.userId),
 *     handle: (data, context) => {
 *       // context.getAggregate() returns Aggregate<InactiveUser>
 *       return ok([UserActivated.create({ 
 *         userId: data.userId,
 *         reason: data.reason,
 *         activatedAt: new Date().toISOString()
 *       })]);
 *     }
 *   }
 * });
 * ```
 */
export function defineCommand<
  TName extends string,
  TSchema extends z.ZodTypeAny,
  TProjector extends IAggregateProjector<TPayloadUnion>,
  TPayloadUnion extends ITypedAggregatePayload,
  TAggregatePayload extends TPayloadUnion = TPayloadUnion
>(
  definition: TAggregatePayload extends TPayloadUnion
    ? CommandSchemaDefinitionWithPayload<TName, TSchema, TProjector, TPayloadUnion, TAggregatePayload>
    : CommandSchemaDefinition<TName, TSchema, TProjector, TPayloadUnion>
): CommandDefinitionResult<TName, TSchema, TProjector, TPayloadUnion, TAggregatePayload>;

export function defineCommand<
  TName extends string,
  TSchema extends z.ZodTypeAny,
  TProjector extends IAggregateProjector<TPayloadUnion>,
  TPayloadUnion extends ITypedAggregatePayload
>(
  definition: CommandSchemaDefinition<TName, TSchema, TProjector, TPayloadUnion> | 
    CommandSchemaDefinitionWithPayload<TName, TSchema, TProjector, TPayloadUnion, any>
) {
  type InferredType = z.infer<TSchema>;
  
  // Create a class that implements ICommandWithHandler
  class SchemaCommand implements ICommandWithHandler<InferredType, TProjector, TPayloadUnion> {
    readonly commandType = definition.type;
    private readonly data: InferredType;
    
    constructor(data: InferredType) {
      // Validate data upfront
      this.data = definition.schema.parse(data) as InferredType;
    }
    
    getProjector(): TProjector {
      return definition.projector;
    }
    
    specifyPartitionKeys(_command: InferredType): PartitionKeys {
      return definition.handlers.specifyPartitionKeys(this.data);
    }
    
    validate(_command: InferredType): Result<void, CommandValidationError> {
      // Schema validation already done in constructor, just run business validation if provided
      if (definition.handlers.validate) {
        return definition.handlers.validate(this.data);
      }
      return ok(undefined);
    }
    
    handle(
      _command: InferredType,
      context: ICommandContext<any>
    ): Result<IEventPayload[], SekibanError> {
      // If required payload type is specified, validate it
      if ('requiredPayloadType' in definition && definition.requiredPayloadType) {
        const aggregateResult = context.getAggregate();
        if (aggregateResult.isOk()) {
          const aggregate = aggregateResult.value;
          if (aggregate.payload.aggregateType !== definition.requiredPayloadType) {
            return err(new CommandValidationError(
              definition.type,
              [`Command requires payload type '${definition.requiredPayloadType}' but found '${aggregate.payload.aggregateType}'`]
            ));
          }
        }
      }
      
      return definition.handlers.handle(this.data, context);
    }
  }
  
  return {
    type: definition.type,
    schema: definition.schema,
    projector: definition.projector,
    requiredPayloadType: 'requiredPayloadType' in definition ? definition.requiredPayloadType : undefined,
    
    /**
     * Create a new command instance that implements ICommandWithHandler
     */
    create: (data: InferredType): SchemaCommand => {
      return new SchemaCommand(data);
    },
    
    /**
     * Validate command data (schema + business rules)
     */
    validate: (data: unknown): Result<void, CommandValidationError> => {
      // First validate schema
      const parseResult = definition.schema.safeParse(data);
      if (!parseResult.success) {
        return err(new CommandValidationError(
          definition.type,
          parseResult.error.errors.map(e => e.message)
        ));
      }
      
      // Then apply business validation if provided
      if (definition.handlers.validate) {
        return definition.handlers.validate(parseResult.data);
      }
      return ok(undefined);
    }
  } as const;
}

/**
 * Result type of defineCommand function
 */
export interface CommandDefinitionResult<
  TName extends string,
  TSchema extends z.ZodTypeAny,
  TProjector extends IAggregateProjector<TPayloadUnion>,
  TPayloadUnion extends ITypedAggregatePayload,
  TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
> {
  type: TName;
  schema: TSchema;
  projector: TProjector;
  requiredPayloadType?: string;
  create: (data: z.infer<TSchema>) => ICommandWithHandler<z.infer<TSchema>, TProjector, TPayloadUnion, TAggregatePayload>;
  validate: (data: unknown) => Result<void, CommandValidationError>;
}

/**
 * Type helper to extract the command definition from a defineCommand result
 */
export type CommandDefinition<T> = T extends CommandDefinitionResult<
  infer TName extends string,
  infer TSchema extends z.ZodTypeAny,
  infer TProjector,
  infer TPayloadUnion extends ITypedAggregatePayload,
  infer TAggregatePayload
>
  ? CommandDefinitionResult<TName, TSchema, TProjector, TPayloadUnion, TAggregatePayload>
  : never;

/**
 * Type helper to extract the command instance type from a defineCommand result
 */
export type InferCommandType<T> = T extends { create: (data: any) => infer R } ? R : never;

/**
 * Helper to create command context for testing
 */
export function createCommandContext<TAggregatePayload extends ITypedAggregatePayload | EmptyAggregatePayload>(
  aggregate: Aggregate<TAggregatePayload>,
  metadata: Metadata = { timestamp: new Date() },
  eventBuilder?: (payload: IEventPayload, version: number) => IEvent
): ICommandContext<TAggregatePayload> {
  const events: IEvent[] = [];
  
  return {
    originalSortableUniqueId: aggregate.lastSortableUniqueId?.toString() || '',
    events,
    partitionKeys: aggregate.partitionKeys,
    metadata,
    
    getPartitionKeys(): PartitionKeys {
      return aggregate.partitionKeys;
    },
    
    getNextVersion(): number {
      return aggregate.version + events.length + 1;
    },
    
    getCurrentVersion(): number {
      return aggregate.version + events.length;
    },
    
    appendEvent(eventPayload: IEventPayload): Result<IEvent, SekibanError> {
      const event = eventBuilder 
        ? eventBuilder(eventPayload, this.getNextVersion())
        : {
            id: { toString: () => `test-${Date.now()}` } as any,
            partitionKeys: aggregate.partitionKeys,
            aggregateType: aggregate.aggregateType,
            eventType: eventPayload.constructor.name,
            version: this.getNextVersion(),
            payload: eventPayload,
            metadata: this.metadata
          };
      
      events.push(event);
      return ok(event);
    },
    
    getService<T>(_serviceType: new (...args: any[]) => T): Result<T, SekibanError> {
      return err(new CommandValidationError('Test', ['Service resolution not implemented in test context']));
    },
    
    getAggregate(): Result<Aggregate<TAggregatePayload>, SekibanError> {
      return ok(aggregate);
    }
  };
}