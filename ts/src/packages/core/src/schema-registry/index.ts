/**
 * Zod-based Schema Registry for Sekiban TypeScript
 * 
 * This module provides a class-free, schema-first approach to defining
 * and managing domain types (events, commands, projectors) using Zod schemas.
 */

// Core schema definition functions
export { defineEvent } from './event-schema';
export { defineCommand } from './command-schema';
export { defineProjector } from './projector-schema';

// Simplified command API
export { command } from './command-api';

// Schema registry and executor
export { SchemaRegistry } from './registry';
export { SchemaExecutor } from './schema-executor';

// Schema-based SekibanDomainTypes implementation
export { createSchemaDomainTypes, createSekibanDomainTypesFromGlobalRegistry } from './schema-domain-types';

// Type exports for advanced usage
export type {
  EventSchemaDefinition,
  EventDefinition,
  InferEventType
} from './event-schema';

export type {
  ICommandContextWithoutState,
  ICommandContext,
  ICommandWithHandler,
  CommandHandlers,
  CommandSchemaDefinition,
  CommandSchemaDefinitionWithPayload,
  CommandDefinitionResult,
  CommandDefinition,
  InferCommandType
} from './command-schema';

export type {
  ProjectionFunction,
  ProjectorDefinition,
  ProjectorDefinitionType,
  InferProjectorPayload
} from './projector-schema';

export type {
  SafeParseResult
} from './registry';

export type {
  SchemaExecutorConfig,
  CommandResponse
} from './schema-executor';

// Import SchemaRegistry for global instance
import { SchemaRegistry } from './registry';

// Create a global registry instance for convenience
export const globalRegistry = new SchemaRegistry();

// Convenience functions that use the global registry
export const registerEvent = <T extends { type: string; schema: any }>(event: T): T => 
  globalRegistry.registerEvent(event);

export const registerCommand = <T extends { type: string }>(command: T): T => 
  globalRegistry.registerCommand(command);

export const registerProjector = <T extends { aggregateType: string }>(projector: T): T => 
  globalRegistry.registerProjector(projector);