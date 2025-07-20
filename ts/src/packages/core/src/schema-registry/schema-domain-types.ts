import type { Result } from 'neverthrow';
import { ok, err } from 'neverthrow';
import type { 
  SekibanDomainTypes,
  IEventTypes,
  ICommandTypes,
  IProjectorTypes,
  IQueryTypes,
  IAggregateTypes,
  ISekibanSerializer,
  EventTypeInfo,
  CommandTypeInfo,
  ProjectorTypeInfo,
  AggregateTypeInfo,
  QueryTypeInfo,
  EventDocument,
  CommandMetadata,
  CommandResult
} from '../domain-types/interfaces.js';
import type { IEvent } from '../events/event.js';
import { createEvent, createEventMetadata } from '../events/event.js';
import type { IEventPayload } from '../events/event-payload.js';
import type { ICommand } from '../commands/command.js';
import type { IAggregatePayload } from '../aggregates/aggregate-payload.js';
import type { AggregateProjector, ITypedAggregatePayload } from '../aggregates/aggregate-projector.js';
import type { ICommandExecutor } from '../commands/executor.js';
import type { IProjector } from '../aggregates/projector-interface.js';
import { PartitionKeys } from '../documents/partition-keys.js';
import { SortableUniqueId } from '../documents/sortable-unique-id.js';
import { SchemaRegistry } from './registry.js';
import { SekibanError, EventStoreError } from '../result/errors.js';
import type { EventDefinition } from './event-schema.js';
import type { CommandDefinition } from './command-schema.js';
import type { ProjectorDefinitionType } from './projector-schema.js';

/**
 * Schema-based implementation of IEventTypes
 */
class SchemaEventTypes implements IEventTypes {
  constructor(private registry: SchemaRegistry) {}

  getEventTypes(): Array<EventTypeInfo> {
    // Schema-based events don't have constructors, so we create synthetic ones
    return this.registry.getEventTypes().map(type => ({
      name: type,
      constructor: class {} as any // Placeholder since schemas don't have constructors
    }));
  }

  getEventTypeByName(name: string): (new (...args: any[]) => IEventPayload) | undefined {
    // Schema-based approach doesn't use constructors
    // This is a compatibility shim
    if (this.registry.hasEventType(name)) {
      return class {} as any;
    }
    return undefined;
  }

  createEvent<T extends IEventPayload>(name: string, payload: unknown): Result<T, Error> {
    try {
      const eventDef = this.registry.getEventDefinition(name);
      if (!eventDef) {
        return err(new Error(`Unknown event type: ${name}`));
      }
      
      const event = eventDef.create(payload);
      return ok(event as T);
    } catch (error) {
      return err(error instanceof Error ? error : new Error('Event creation failed'));
    }
  }

  deserializeEvent(document: EventDocument): Result<IEvent, SekibanError> {
    try {
      const deserialized = this.registry.deserializeEvent(document.eventType, document.payload);
      
      const event = createEvent({
        id: SortableUniqueId.fromString(document.id).unwrapOr(SortableUniqueId.generate()),
        partitionKeys: PartitionKeys.create(
          document.aggregateId,
          document.aggregateType,
          document.rootPartitionKey || ''
        ),
        eventType: document.eventType,
        payload: deserialized,
        aggregateType: document.aggregateType,
        version: document.version,
        metadata: createEventMetadata({
          timestamp: new Date(document.timestamp),
          ...document.metadata
        })
      });
      
      return ok(event);
    } catch (error) {
      return err(error instanceof SekibanError ? error : new EventStoreError('deserialize', error instanceof Error ? error.message : 'Event deserialization failed'));
    }
  }

  serializeEvent(event: IEvent<IEventPayload>): any {
    return {
      id: event.id.toString(),
      eventType: event.eventType,
      aggregateId: event.partitionKeys.aggregateId,
      aggregateType: event.aggregateType,
      payload: event.payload,
      metadata: event.metadata,
      timestamp: event.metadata.timestamp,
      version: event.version
    };
  }
}

/**
 * Schema-based implementation of ICommandTypes
 */
class SchemaCommandTypes implements ICommandTypes {
  constructor(private registry: SchemaRegistry) {}

  getCommandTypes(): Array<CommandTypeInfo> {
    return this.registry.getCommandTypes().map(type => ({
      name: type,
      constructor: class {} as any // Placeholder
    }));
  }

  getCommandTypeByName(name: string): { name: string; constructor: any } | undefined {
    if (this.registry.hasCommandType(name)) {
      return {
        name,
        constructor: class {} as any
      };
    }
    return undefined;
  }

  createCommand(type: string, payload: any): ICommand<any> {
    const commandDef = this.registry.getCommand(type);
    if (!commandDef || !('create' in commandDef)) {
      throw new Error(`Unknown command type: ${type}`);
    }
    return commandDef.create(payload);
  }

  async executeCommand(
    executor: ICommandExecutor,
    command: unknown,
    metadata: CommandMetadata
  ): Promise<Result<CommandResult, SekibanError>> {
    // This would need to be implemented based on schema command execution
    // For now, return an error indicating this needs implementation
    return err(new EventStoreError('command', 'Schema-based command execution not yet implemented'));
  }

  /**
   * Get the aggregate type for a command
   */
  getAggregateTypeForCommand(commandType: string): string | undefined {
    const commandDef = this.registry.getCommand(commandType);
    if (commandDef && 'aggregateType' in commandDef) {
      return commandDef.aggregateType;
    }
    return undefined;
  }
}

/**
 * Schema-based implementation of IProjectorTypes
 */
class SchemaProjectorTypes implements IProjectorTypes {
  constructor(private registry: SchemaRegistry) {}

  getProjectorTypes(): Array<{ aggregateTypeName: string; projector: IProjector<any> }> {
    return this.registry.getProjectorTypes().map(type => {
      const projector = this.registry.getProjector(type);
      return {
        aggregateTypeName: type,
        projector: projector as IProjector<any>
      };
    });
  }

  getProjectorByName(name: string): (new (...args: any[]) => AggregateProjector<ITypedAggregatePayload>) | undefined {
    if (this.registry.hasProjectorType(name)) {
      return class {} as any;
    }
    return undefined;
  }

  getProjectorByAggregateType(aggregateType: string): IProjector<any> | undefined {
    const projector = this.registry.getProjector(aggregateType);
    return projector as any;
  }
}

/**
 * Schema-based implementation of IAggregateTypes
 */
class SchemaAggregateTypes implements IAggregateTypes {
  constructor(private registry: SchemaRegistry) {}

  getAggregateTypes(): Array<AggregateTypeInfo> {
    // In schema-based approach, aggregate types come from projectors
    return this.registry.getProjectorTypes().map(type => ({
      name: type,
      constructor: class {} as any
    }));
  }

  getAggregateTypeByName(name: string): (new (...args: any[]) => IAggregatePayload) | undefined {
    if (this.registry.hasProjectorType(name)) {
      return class {} as any;
    }
    return undefined;
  }
}

/**
 * Schema-based implementation of IQueryTypes
 */
class SchemaQueryTypes implements IQueryTypes {
  constructor(private registry: SchemaRegistry) {}

  getQueryTypes(): Array<QueryTypeInfo> {
    // Schema-based approach doesn't currently have query types
    // This would need to be extended
    return [];
  }

  getQueryTypeByName(name: string): (new (...args: any[]) => any) | undefined {
    return undefined;
  }
}

/**
 * Schema-based serializer using Zod validation
 */
class SchemaSerializer implements ISekibanSerializer {
  constructor(private registry: SchemaRegistry) {}

  serialize(value: any): string {
    return JSON.stringify(value);
  }

  deserialize<T>(json: string, type?: new (...args: any[]) => T): T {
    return JSON.parse(json);
  }
}

/**
 * Create a SekibanDomainTypes instance from a SchemaRegistry
 */
export function createSchemaDomainTypes(registry: SchemaRegistry): SekibanDomainTypes {
  return {
    eventTypes: new SchemaEventTypes(registry),
    commandTypes: new SchemaCommandTypes(registry),
    projectorTypes: new SchemaProjectorTypes(registry),
    aggregateTypes: new SchemaAggregateTypes(registry),
    queryTypes: new SchemaQueryTypes(registry),
    serializer: new SchemaSerializer(registry)
  };
}

/**
 * Create a SekibanDomainTypes instance from the global schema registry
 */
export function createSekibanDomainTypesFromGlobalRegistry(): SekibanDomainTypes {
  // Import the global registry instance
  const { globalRegistry } = require('./index.js');
  return createSchemaDomainTypes(globalRegistry);
}