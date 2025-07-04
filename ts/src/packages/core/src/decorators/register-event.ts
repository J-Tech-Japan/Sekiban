import { GlobalRegistry } from '../domain-types/registry.js';
import type { IEventPayload } from '../events/event-payload.js';

export type EventConstructor = new (...args: any[]) => IEventPayload;

/**
 * Decorator to register an event type with the Sekiban type registry.
 * 
 * @param eventType - Optional custom event type name. If not provided, uses the class name.
 * 
 * @example
 * ```typescript
 * @RegisterEvent('UserCreated')
 * export class UserCreated implements IEventPayload {
 *   constructor(public readonly name: string) {}
 * }
 * ```
 */
export function RegisterEvent(eventType?: string) {
  return function <T extends EventConstructor>(constructor: T) {
    const typeName = eventType || constructor.name;
    GlobalRegistry.registerEvent(typeName, constructor);
    return constructor;
  };
}