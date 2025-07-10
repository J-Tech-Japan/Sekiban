import { z } from 'zod';
import type { EventDefinition } from './event-schema.js';
import type { CommandDefinition } from './command-schema.js';
import type { ProjectorDefinitionType } from './projector-schema.js';

/**
 * Result type for safe deserialization
 */
export type SafeParseResult<T> = 
  | { success: true; data: T }
  | { success: false; error: z.ZodError };

/**
 * Schema registry for managing domain types
 * 
 * This class provides a centralized registry for events, commands, and projectors
 * using Zod schemas for runtime validation and type safety.
 */
export class SchemaRegistry {
  private eventSchemas = new Map<string, z.ZodTypeAny>();
  private eventDefinitions = new Map<string, any>();
  private commandDefinitions = new Map<string, any>();
  private projectorDefinitions = new Map<string, any>();

  /**
   * Register an event definition with the registry
   */
  registerEvent<T extends { type: string; schema: z.ZodTypeAny }>(event: T): T {
    this.eventSchemas.set(event.type, event.schema);
    this.eventDefinitions.set(event.type, event);
    return event;
  }

  /**
   * Register a command definition with the registry
   */
  registerCommand<T extends { type: string }>(command: T): T {
    this.commandDefinitions.set(command.type, command);
    return command;
  }

  /**
   * Register a projector definition with the registry
   */
  registerProjector<T extends { aggregateType: string }>(projector: T): T {
    this.projectorDefinitions.set(projector.aggregateType, projector);
    return projector;
  }

  /**
   * Get an event schema by type name
   */
  getEventSchema(eventType: string): z.ZodTypeAny | undefined {
    return this.eventSchemas.get(eventType);
  }

  /**
   * Get an event definition by type name
   */
  getEventDefinition(eventType: string): any {
    return this.eventDefinitions.get(eventType);
  }

  /**
   * Get a command definition by type name
   */
  getCommand(commandType: string): any {
    return this.commandDefinitions.get(commandType);
  }

  /**
   * Get a projector definition by aggregate type
   */
  getProjector(aggregateType: string): any {
    return this.projectorDefinitions.get(aggregateType);
  }

  /**
   * Deserialize and validate event data
   */
  deserializeEvent(eventType: string, data: unknown): any {
    const schema = this.eventSchemas.get(eventType);
    if (!schema) {
      throw new Error(`Unknown event type: ${eventType}`);
    }
    
    const parsed = schema.parse(data);
    return { type: eventType, ...parsed };
  }

  /**
   * Safely deserialize event data without throwing
   */
  safeDeserializeEvent(eventType: string, data: unknown): SafeParseResult<any> {
    const schema = this.eventSchemas.get(eventType);
    if (!schema) {
      return {
        success: false,
        error: new z.ZodError([{
          code: 'custom',
          message: `Unknown event type: ${eventType}`,
          path: []
        }])
      };
    }

    const result = schema.safeParse(data);
    if (result.success) {
      return {
        success: true,
        data: { type: eventType, ...result.data }
      };
    }
    
    return result;
  }

  /**
   * Get all registered event type names
   */
  getEventTypes(): string[] {
    return Array.from(this.eventSchemas.keys());
  }

  /**
   * Alias for getEventTypes for compatibility
   */
  getRegisteredEvents(): string[] {
    return this.getEventTypes();
  }

  /**
   * Get all registered command type names
   */
  getCommandTypes(): string[] {
    return Array.from(this.commandDefinitions.keys());
  }

  /**
   * Alias for getCommandTypes for compatibility
   */
  getRegisteredCommands(): string[] {
    return this.getCommandTypes();
  }

  /**
   * Get all registered projector aggregate type names
   */
  getProjectorTypes(): string[] {
    return Array.from(this.projectorDefinitions.keys());
  }

  /**
   * Clear all registrations
   */
  clear(): void {
    this.eventSchemas.clear();
    this.eventDefinitions.clear();
    this.commandDefinitions.clear();
    this.projectorDefinitions.clear();
  }

  /**
   * Get registration counts for debugging
   */
  getRegistrationCounts(): {
    events: number;
    commands: number;
    projectors: number;
  } {
    return {
      events: this.eventSchemas.size,
      commands: this.commandDefinitions.size,
      projectors: this.projectorDefinitions.size
    };
  }

  /**
   * Check if an event type is registered
   */
  hasEventType(eventType: string): boolean {
    return this.eventSchemas.has(eventType);
  }

  /**
   * Check if a command type is registered
   */
  hasCommandType(commandType: string): boolean {
    return this.commandDefinitions.has(commandType);
  }

  /**
   * Check if a projector is registered for an aggregate type
   */
  hasProjectorType(aggregateType: string): boolean {
    return this.projectorDefinitions.has(aggregateType);
  }

  /**
   * Validate data against an event schema without creating an event
   */
  validateEventData(eventType: string, data: unknown): z.SafeParseReturnType<any, any> {
    const schema = this.eventSchemas.get(eventType);
    if (!schema) {
      return {
        success: false,
        error: new z.ZodError([{
          code: 'custom',
          message: `Unknown event type: ${eventType}`,
          path: []
        }])
      };
    }

    return schema.safeParse(data);
  }

  /**
   * Get all event types with their schemas for introspection
   */
  getAllEventSchemas(): Array<{ type: string; schema: z.ZodTypeAny }> {
    return Array.from(this.eventSchemas.entries()).map(([type, schema]) => ({
      type,
      schema
    }));
  }

  /**
   * Get all command definitions for introspection
   */
  getAllCommandDefinitions(): Array<{ type: string; definition: any }> {
    return Array.from(this.commandDefinitions.entries()).map(([type, definition]) => ({
      type,
      definition
    }));
  }

  /**
   * Get all projector definitions for introspection
   */
  getAllProjectorDefinitions(): Array<{ aggregateType: string; definition: any }> {
    return Array.from(this.projectorDefinitions.entries()).map(([aggregateType, definition]) => ({
      aggregateType,
      definition
    }));
  }

}