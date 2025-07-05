import { z } from 'zod';

/**
 * Definition structure for an event schema
 */
export interface EventSchemaDefinition<TName extends string, TSchema extends z.ZodTypeAny> {
  type: TName;
  schema: TSchema;
}

/**
 * Define an event with a Zod schema
 * 
 * @param definition - The event definition including type name and Zod schema
 * @returns Event definition object with create, parse, and safeParse methods
 * 
 * @example
 * ```typescript
 * const UserCreated = defineEvent({
 *   type: 'UserCreated',
 *   schema: z.object({
 *     userId: z.string(),
 *     name: z.string()
 *   })
 * });
 * 
 * const event = UserCreated.create({ userId: '123', name: 'John' });
 * ```
 */
export function defineEvent<TName extends string, TSchema extends z.ZodTypeAny>(
  definition: EventSchemaDefinition<TName, TSchema>
) {
  type InferredType = z.infer<TSchema>;
  type EventType = InferredType & { type: TName };

  return {
    type: definition.type,
    schema: definition.schema,
    
    /**
     * Create a new event instance with type discriminator
     */
    create: (data: InferredType): EventType => ({
      type: definition.type,
      ...data
    } as EventType),
    
    /**
     * Parse and validate data, throwing on validation failure
     */
    parse: (data: unknown): EventType => {
      const parsed = definition.schema.parse(data);
      return {
        type: definition.type,
        ...parsed
      } as EventType;
    },
    
    /**
     * Safe parse that returns a result object instead of throwing
     */
    safeParse: (data: unknown): z.SafeParseReturnType<InferredType, EventType> => {
      const result = definition.schema.safeParse(data);
      if (result.success) {
        return {
          success: true,
          data: {
            type: definition.type,
            ...result.data
          } as EventType
        };
      }
      return result as z.SafeParseReturnType<InferredType, EventType>;
    }
  } as const;
}

/**
 * Type helper to extract the event type from a defineEvent result
 */
export type EventDefinition<T> = T extends ReturnType<typeof defineEvent<infer TName, infer TSchema>>
  ? ReturnType<typeof defineEvent<TName, TSchema>>
  : never;

/**
 * Type helper to extract the event data type from a defineEvent result
 */
export type InferEventType<T> = T extends { create: (data: any) => infer R } ? R : never;