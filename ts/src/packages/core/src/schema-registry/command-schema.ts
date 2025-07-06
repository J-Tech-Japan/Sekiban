import { z } from 'zod';
import { err, type Result } from 'neverthrow';
import type { PartitionKeys } from '../documents/partition-keys.js';
import type { IEventPayload } from '../events/event-payload.js';
import type { Aggregate } from '../aggregates/aggregate.js';
import type { EmptyAggregatePayload } from '../aggregates/aggregate.js';
import type { SekibanError } from '../result/errors.js';
import { CommandValidationError } from '../result/errors.js';
import type { ITypedAggregatePayload } from '../aggregates/aggregate-projector.js';
import type { ICommand } from '../commands/command.js';

/**
 * Command handlers for business logic
 */
export interface CommandHandlers<TData, TPayloadUnion extends ITypedAggregatePayload> {
  /**
   * Specify partition keys for the command
   */
  specifyPartitionKeys: (data: TData) => PartitionKeys;
  
  /**
   * Perform business validation beyond schema validation
   */
  validate: (data: TData) => Result<void, CommandValidationError>;
  
  /**
   * Handle the command and return events
   */
  handle: (
    data: TData,
    aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>
  ) => Result<IEventPayload[], SekibanError>;
}

/**
 * Definition structure for a command schema
 */
export interface CommandSchemaDefinition<
  TName extends string,
  TSchema extends z.ZodTypeAny,
  TPayloadUnion extends ITypedAggregatePayload
> {
  type: TName;
  schema: TSchema;
  aggregateType: string; // Explicitly specify which aggregate type this command targets
  handlers: CommandHandlers<z.infer<TSchema>, TPayloadUnion>;
}

/**
 * Define a command with a Zod schema and handlers
 * 
 * @param definition - The command definition including type name, schema, and handlers
 * @returns Command definition object with create, validate, and execute methods
 * 
 * @example
 * ```typescript
 * const CreateUser = defineCommand({
 *   type: 'CreateUser',
 *   schema: z.object({
 *     name: z.string().min(1),
 *     email: z.string().email()
 *   }),
 *   handlers: {
 *     specifyPartitionKeys: () => PartitionKeys.generate('User'),
 *     validate: (data) => {
 *       // Business validation
 *       if (data.email.endsWith('@test.com')) {
 *         return err(new CommandValidationError('CreateUser', ['Test emails not allowed']));
 *       }
 *       return ok(undefined);
 *     },
 *     handle: (data, aggregate) => {
 *       // Command logic
 *       return ok([UserCreated.create({ ... })]);
 *     }
 *   }
 * });
 * ```
 */
export function defineCommand<
  TName extends string,
  TSchema extends z.ZodTypeAny,
  TPayloadUnion extends ITypedAggregatePayload
>(definition: CommandSchemaDefinition<TName, TSchema, TPayloadUnion>) {
  type InferredType = z.infer<TSchema>;
  type CommandType = InferredType & { commandType: TName };

  return {
    type: definition.type,
    schema: definition.schema,
    aggregateType: definition.aggregateType,
    handlers: definition.handlers,
    
    /**
     * Create a new command instance that implements ICommand interface
     */
    create: (data: InferredType): ICommand<TPayloadUnion> => {
      // Validate data upfront
      const validatedData = definition.schema.parse(data) as InferredType;
      
      // Return an object that implements ICommand
      return {
        commandType: definition.type,
        ...validatedData,
        
        specifyPartitionKeys(): PartitionKeys {
          return definition.handlers.specifyPartitionKeys(validatedData);
        },
        
        validate(): Result<void, CommandValidationError> {
          // Schema validation already done in create, just run business validation
          return definition.handlers.validate(validatedData);
        },
        
        handle(aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>): Result<IEventPayload[], SekibanError> {
          return definition.handlers.handle(validatedData, aggregate);
        }
      } as ICommand<TPayloadUnion>;
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
      
      // Then apply business validation
      return definition.handlers.validate(parseResult.data);
    },
    
    /**
     * Execute the command with validated data
     */
    execute: (
      data: InferredType,
      aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>
    ): Result<IEventPayload[], SekibanError> => {
      return definition.handlers.handle(data, aggregate);
    }
  } as const;
}

/**
 * Type helper to extract the command type from a defineCommand result
 */
export type CommandDefinition<T> = T extends ReturnType<typeof defineCommand<infer TName extends string, infer TSchema extends z.ZodTypeAny, infer TPayload extends ITypedAggregatePayload>>
  ? ReturnType<typeof defineCommand<TName, TSchema, TPayload>>
  : never;

/**
 * Type helper to extract the command data type from a defineCommand result
 */
export type InferCommandType<T> = T extends { create: (data: any) => infer R } ? R : never;