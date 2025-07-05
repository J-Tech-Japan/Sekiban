/**
 * Zod-based Schema Registry for Sekiban TypeScript
 * 
 * This module provides a class-free, schema-first approach to defining
 * and managing domain types (events, commands, projectors) using Zod schemas.
 */

// Core schema definition functions
export { defineEvent } from './event-schema.js';
export { defineCommand } from './command-schema.js';
export { defineProjector } from './projector-schema.js';

// Schema registry and executor
export { SchemaRegistry } from './registry.js';
export { SchemaExecutor } from './schema-executor.js';

// Schema-based SekibanDomainTypes implementation
export { createSchemaDomainTypes, createSekibanDomainTypesFromGlobalRegistry } from './schema-domain-types.js';

// Type exports for advanced usage
export type {
  EventSchemaDefinition,
  EventDefinition,
  InferEventType
} from './event-schema.js';

export type {
  CommandHandlers,
  CommandSchemaDefinition,
  CommandDefinition,
  InferCommandType
} from './command-schema.js';

export type {
  ProjectionFunction,
  ProjectorDefinition,
  ProjectorDefinitionType,
  InferProjectorPayload
} from './projector-schema.js';

export type {
  SafeParseResult
} from './registry.js';

export type {
  SchemaExecutorConfig,
  CommandResponse
} from './schema-executor.js';

// Import SchemaRegistry for global instance
import { SchemaRegistry } from './registry.js';

// Create a global registry instance for convenience
export const globalRegistry = new SchemaRegistry();

// Convenience functions that use the global registry
export const registerEvent = <T extends { type: string; schema: any }>(event: T): T => 
  globalRegistry.registerEvent(event);

export const registerCommand = <T extends { type: string }>(command: T): T => 
  globalRegistry.registerCommand(command);

export const registerProjector = <T extends { aggregateType: string }>(projector: T): T => 
  globalRegistry.registerProjector(projector);