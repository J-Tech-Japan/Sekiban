/**
 * Marker interface for event payloads in the event sourcing system.
 * 
 * Any object can implement this interface to be used as an event payload.
 * This is intentionally an empty interface (phantom type) to provide
 * type safety without imposing structure constraints.
 * 
 * @example
 * ```typescript
 * class UserCreated implements IEventPayload {
 *   constructor(
 *     public readonly userId: string,
 *     public readonly email: string
 *   ) {}
 * }
 * ```
 */
export interface IEventPayload {}

/**
 * Type guard to check if a value can be used as an event payload.
 * Returns true for objects (including arrays and dates), false for primitives.
 * 
 * @param value - The value to check
 * @returns True if the value is an object (non-null), false otherwise
 */
export function isEventPayload(value: unknown): value is IEventPayload {
  return typeof value === 'object' && value !== null;
}