import { GlobalRegistry } from '../domain-types/registry.js';
import type { IAggregatePayload } from '../aggregates/aggregate-payload.js';

export type AggregateConstructor = new (...args: any[]) => IAggregatePayload;

/**
 * Decorator to register an aggregate payload type with the Sekiban type registry.
 * 
 * @param aggregateType - Optional custom aggregate type name. If not provided, uses the class name.
 * 
 * @example
 * ```typescript
 * @RegisterAggregate('UserAggregate')
 * export class UserAggregate implements IAggregatePayload {
 *   readonly aggregateType = 'User';
 *   // ...
 * }
 * ```
 */
export function RegisterAggregate(aggregateType?: string) {
  return function <T extends AggregateConstructor>(constructor: T) {
    const typeName = aggregateType || constructor.name;
    GlobalRegistry.registerAggregate(typeName, constructor);
    return constructor;
  };
}