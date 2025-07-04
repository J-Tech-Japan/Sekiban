import { GlobalRegistry } from '../domain-types/registry.js';

export type QueryConstructor = new (...args: any[]) => any;

/**
 * Decorator to register a query type with the Sekiban type registry.
 * 
 * @param queryType - Optional custom query type name. If not provided, uses the class name.
 * 
 * @example
 * ```typescript
 * @RegisterQuery('GetUserById')
 * export class GetUserByIdQuery {
 *   constructor(public readonly userId: string) {}
 * }
 * ```
 */
export function RegisterQuery(queryType?: string) {
  return function <T extends QueryConstructor>(constructor: T) {
    const typeName = queryType || constructor.name;
    GlobalRegistry.registerQuery(typeName, constructor);
    return constructor;
  };
}