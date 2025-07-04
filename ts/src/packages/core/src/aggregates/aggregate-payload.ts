/**
 * Marker interface for aggregate payloads in the event sourcing system.
 * 
 * Aggregates represent the current state of an entity, projected from
 * a series of events. Any object can implement this interface to be
 * used as an aggregate payload.
 * 
 * This is intentionally an empty interface (phantom type) to provide
 * type safety without imposing structure constraints, allowing maximum
 * flexibility in aggregate design.
 * 
 * @example
 * ```typescript
 * class User implements IAggregatePayload {
 *   constructor(
 *     public readonly id: string,
 *     public readonly email: string,
 *     public readonly isActive: boolean
 *   ) {}
 * }
 * ```
 */
export interface IAggregatePayload {}

/**
 * Type guard to check if a value can be used as an aggregate payload.
 * Returns true for objects (including arrays and dates), false for primitives.
 * 
 * @param value - The value to check
 * @returns True if the value is an object (non-null), false otherwise
 */
export function isAggregatePayload(value: unknown): value is IAggregatePayload {
  return typeof value === 'object' && value !== null;
}