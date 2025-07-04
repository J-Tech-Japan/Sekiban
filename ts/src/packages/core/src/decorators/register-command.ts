import { GlobalRegistry } from '../domain-types/registry.js';
import type { ICommand } from '../commands/command.js';
import type { IAggregatePayload } from '../aggregates/aggregate-payload.js';

export type CommandConstructor = new (...args: any[]) => ICommand<IAggregatePayload>;

/**
 * Decorator to register a command type with the Sekiban type registry.
 * 
 * @param commandType - Optional custom command type name. If not provided, uses the class name.
 * 
 * @example
 * ```typescript
 * @RegisterCommand('CreateUser')
 * export class CreateUserCommand implements ICommand<UserAggregate> {
 *   // ...
 * }
 * ```
 */
export function RegisterCommand(commandType?: string) {
  return function <T extends CommandConstructor>(constructor: T) {
    const typeName = commandType || constructor.name;
    GlobalRegistry.registerCommand(typeName, constructor);
    return constructor;
  };
}