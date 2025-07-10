import { z } from 'zod';
import { ok, err, type Result } from 'neverthrow';
import { defineCommand, CommandSchemaDefinition, CommandSchemaDefinitionWithPayload } from './command-schema.js';
import { TypedPartitionKeys, type PartitionKeys } from '../documents/index.js';
import type { IEventPayload } from '../events/event-payload.js';
import type { ITypedAggregatePayload, IAggregateProjector } from '../aggregates/aggregate-projector.js';
import type { SekibanError } from '../result/errors.js';
import { CommandValidationError } from '../result/errors.js';
import type { EmptyAggregatePayload } from '../aggregates/aggregate.js';
import type { ICommandContext } from './command-schema.js';

/**
 * Simplified context for command handlers
 */
export interface SimplifiedContext<TAggregatePayload extends ITypedAggregatePayload | EmptyAggregatePayload> {
  /**
   * The current aggregate ID
   */
  aggregateId: string;
  
  /**
   * The current aggregate (if exists)
   */
  aggregate?: TAggregatePayload;
  
  /**
   * Append an event to the event stream
   */
  appendEvent: (event: IEventPayload) => void;
  
  /**
   * Get a service by type
   */
  getService: <T>(serviceType: new (...args: any[]) => T) => T | undefined;
}

/**
 * Options for create command
 */
export interface CreateCommandOptions<
  TSchema extends z.ZodTypeAny,
  TProjector extends IAggregateProjector<TPayloadUnion>,
  TPayloadUnion extends ITypedAggregatePayload
> {
  schema: TSchema;
  projector: TProjector;
  partitionKeys: (data: z.infer<TSchema>) => PartitionKeys;
  validate?: (data: z.infer<TSchema>) => Result<void, SekibanError>;
  handle: (data: z.infer<TSchema>, context: SimplifiedContext<EmptyAggregatePayload>) => void;
}

/**
 * Options for update command
 */
export interface UpdateCommandOptions<
  TSchema extends z.ZodTypeAny,
  TProjector extends IAggregateProjector<TPayloadUnion>,
  TPayloadUnion extends ITypedAggregatePayload
> {
  schema: TSchema;
  projector: TProjector;
  partitionKeys: (data: z.infer<TSchema>) => PartitionKeys;
  validate?: (data: z.infer<TSchema>) => Result<void, SekibanError>;
  handle: (data: z.infer<TSchema>, context: SimplifiedContext<TPayloadUnion>) => void;
}

/**
 * Options for transition command
 */
export interface TransitionCommandOptions<
  TSchema extends z.ZodTypeAny,
  TProjector extends IAggregateProjector<TPayloadUnion>,
  TPayloadUnion extends ITypedAggregatePayload,
  TFromState extends TPayloadUnion
> {
  schema: TSchema;
  projector: TProjector;
  fromState: string;
  partitionKeys: (data: z.infer<TSchema>) => PartitionKeys;
  validate?: (data: z.infer<TSchema>) => Result<void, SekibanError>;
  handle: (data: z.infer<TSchema>, context: SimplifiedContext<TFromState>) => void;
}

/**
 * Simplified command API
 */
export const command = {
  /**
   * Define a command that creates a new aggregate
   * 
   * @example
   * ```typescript
   * const CreateUser = command.create('CreateUser', {
   *   schema: z.object({
   *     name: z.string(),
   *     email: z.string(),
   *     tenantId: z.string().optional()
   *   }),
   *   projector: UserProjector,
   *   partitionKeys: (data) => data.tenantId 
   *     ? TypedPartitionKeys.Generate(UserProjector, data.tenantId)
   *     : TypedPartitionKeys.Generate(UserProjector),
   *   handle: (data, { aggregateId, appendEvent }) => {
   *     appendEvent(UserCreated.create({
   *       userId: aggregateId,
   *       name: data.name,
   *       email: data.email
   *     }));
   *   }
   * });
   * ```
   */
  create<
    TName extends string,
    TSchema extends z.ZodTypeAny,
    TProjector extends IAggregateProjector<TPayloadUnion>,
    TPayloadUnion extends ITypedAggregatePayload
  >(
    type: TName,
    options: CreateCommandOptions<TSchema, TProjector, TPayloadUnion>
  ) {
    const definition: CommandSchemaDefinition<TName, TSchema, TProjector, TPayloadUnion> = {
      type,
      schema: options.schema,
      projector: options.projector,
      handlers: {
        specifyPartitionKeys: options.partitionKeys,
        validate: options.validate ? (data) => {
          const result = options.validate!(data);
          if (result.isErr()) {
            const error = result.error;
            if (error instanceof CommandValidationError) {
              return err(error);
            }
            return err(new CommandValidationError(type, [error.message]));
          }
          return ok(undefined);
        } : undefined,
        handle: (data: z.infer<TSchema>, context: ICommandContext<TPayloadUnion | EmptyAggregatePayload>) => {
          const events: IEventPayload[] = [];
          const simplifiedContext: SimplifiedContext<EmptyAggregatePayload> = {
            aggregateId: context.getPartitionKeys().aggregateId,
            aggregate: undefined,
            appendEvent: (event) => events.push(event),
            getService: (serviceType) => {
              const result = context.getService(serviceType);
              return result.isOk() ? result.value : undefined;
            }
          };
          
          options.handle(data, simplifiedContext);
          return ok(events);
        }
      }
    };
    return defineCommand(definition as any);
  },
  
  /**
   * Define a command that updates an existing aggregate
   * 
   * @example
   * ```typescript
   * const UpdateUser = command.update('UpdateUser', {
   *   schema: z.object({
   *     userId: z.string(),
   *     name: z.string()
   *   }),
   *   projector: UserProjector,
   *   partitionKeys: (data) => TypedPartitionKeys.Existing(UserProjector, data.userId),
   *   handle: (data, { appendEvent }) => {
   *     appendEvent(UserUpdated.create({
   *       userId: data.userId,
   *       name: data.name
   *     }));
   *   }
   * });
   * ```
   */
  update<
    TName extends string,
    TSchema extends z.ZodTypeAny,
    TProjector extends IAggregateProjector<TPayloadUnion>,
    TPayloadUnion extends ITypedAggregatePayload
  >(
    type: TName,
    options: UpdateCommandOptions<TSchema, TProjector, TPayloadUnion>
  ) {
    const definition: CommandSchemaDefinition<TName, TSchema, TProjector, TPayloadUnion> = {
      type,
      schema: options.schema,
      projector: options.projector,
      handlers: {
        specifyPartitionKeys: options.partitionKeys,
        validate: options.validate ? (data) => {
          const result = options.validate!(data);
          if (result.isErr()) {
            const error = result.error;
            if (error instanceof CommandValidationError) {
              return err(error);
            }
            return err(new CommandValidationError(type, [error.message]));
          }
          return ok(undefined);
        } : undefined,
        handle: (data: z.infer<TSchema>, context: ICommandContext<TPayloadUnion | EmptyAggregatePayload>) => {
          const events: IEventPayload[] = [];
          const aggregateResult = context.getAggregate();
          const aggregate = aggregateResult.isOk() ? aggregateResult.value : null;
          
          const simplifiedContext: SimplifiedContext<TPayloadUnion> = {
            aggregateId: context.getPartitionKeys().aggregateId,
            aggregate: aggregate?.payload as TPayloadUnion,
            appendEvent: (event) => events.push(event),
            getService: (serviceType) => {
              const result = context.getService(serviceType);
              return result.isOk() ? result.value : undefined;
            }
          };
          
          options.handle(data, simplifiedContext);
          return ok(events);
        }
      }
    };
    return defineCommand(definition as any);
  },
  
  /**
   * Define a command that transitions an aggregate from one state to another
   * 
   * @example
   * ```typescript
   * const ActivateUser = command.transition('ActivateUser', {
   *   schema: z.object({
   *     userId: z.string(),
   *     reason: z.string()
   *   }),
   *   projector: UserProjector,
   *   fromState: 'InactiveUser',
   *   partitionKeys: (data) => TypedPartitionKeys.Existing(UserProjector, data.userId),
   *   handle: (data, { aggregate, appendEvent }) => {
   *     // aggregate is typed as InactiveUser
   *     appendEvent(UserActivated.create({
   *       userId: data.userId,
   *       reason: data.reason,
   *       activatedAt: new Date().toISOString()
   *     }));
   *   }
   * });
   * ```
   */
  transition<
    TName extends string,
    TSchema extends z.ZodTypeAny,
    TProjector extends IAggregateProjector<TPayloadUnion>,
    TPayloadUnion extends ITypedAggregatePayload,
    TFromState extends TPayloadUnion
  >(
    type: TName,
    options: TransitionCommandOptions<TSchema, TProjector, TPayloadUnion, TFromState>
  ) {
    const definition: CommandSchemaDefinitionWithPayload<TName, TSchema, TProjector, TPayloadUnion, TFromState> = {
      type,
      schema: options.schema,
      projector: options.projector,
      requiredPayloadType: options.fromState,
      handlers: {
        specifyPartitionKeys: options.partitionKeys,
        validate: options.validate ? (data) => {
          const result = options.validate!(data);
          if (result.isErr()) {
            const error = result.error;
            if (error instanceof CommandValidationError) {
              return err(error);
            }
            return err(new CommandValidationError(type, [error.message]));
          }
          return ok(undefined);
        } : undefined,
        handle: (data: z.infer<TSchema>, context: ICommandContext<TFromState>) => {
          const events: IEventPayload[] = [];
          const aggregateResult = context.getAggregate();
          const aggregate = aggregateResult.isOk() ? aggregateResult.value : null;
          
          const simplifiedContext: SimplifiedContext<TFromState> = {
            aggregateId: context.getPartitionKeys().aggregateId,
            aggregate: aggregate?.payload as TFromState,
            appendEvent: (event) => events.push(event),
            getService: (serviceType) => {
              const result = context.getService(serviceType);
              return result.isOk() ? result.value : undefined;
            }
          };
          
          options.handle(data, simplifiedContext);
          return ok(events);
        }
      }
    };
    return defineCommand(definition as any);
  }
};