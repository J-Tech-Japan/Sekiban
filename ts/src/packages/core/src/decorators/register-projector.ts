import { GlobalRegistry } from '../domain-types/registry.js';
import type { AggregateProjector } from '../aggregates/aggregate-projector.js';
import type { IAggregatePayload } from '../aggregates/aggregate-payload.js';

export type ProjectorConstructor = new (...args: any[]) => AggregateProjector<IAggregatePayload>;

/**
 * Decorator to register a projector type with the Sekiban type registry.
 * 
 * @param projectorType - Optional custom projector type name. If not provided, uses the class name.
 * 
 * @example
 * ```typescript
 * @RegisterProjector('UserProjector')
 * export class UserProjector extends AggregateProjector<UserAggregate> {
 *   // ...
 * }
 * ```
 */
export function RegisterProjector(projectorType?: string) {
  return function <T extends ProjectorConstructor>(constructor: T) {
    const typeName = projectorType || constructor.name;
    GlobalRegistry.registerProjector(typeName, constructor);
    return constructor;
  };
}