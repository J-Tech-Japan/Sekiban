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
  IMultiProjectorTypes,
  EventTypeInfo,
  CommandTypeInfo,
  ProjectorTypeInfo,
  AggregateTypeInfo,
  QueryTypeInfo,
  EventDocument,
  CommandMetadata,
  CommandResult
} from '../domain-types/interfaces';
import type { IEvent } from '../events/event';
import { createEvent, createEventMetadata } from '../events/event';
import type { IEventPayload } from '../events/event-payload';
import type { ICommand } from '../commands/command';
import type { IAggregatePayload } from '../aggregates/aggregate-payload';
import type { AggregateProjector, ITypedAggregatePayload, IAggregateProjector } from '../aggregates/aggregate-projector';
import type { ICommandExecutor } from '../commands/executor';
import type { IProjector } from '../aggregates/projector-interface';
import { PartitionKeys } from '../documents/partition-keys';
import { SortableUniqueId } from '../documents/sortable-unique-id';
import { SchemaRegistry } from './registry';
import { SekibanError, EventStoreError } from '../result/errors';
import type { EventDefinition } from './event-schema';
import type { CommandDefinition } from './command-schema';
import type { ProjectorDefinitionType } from './projector-schema';
import type { IMultiProjector, IMultiProjectorCommon } from '../projectors/multi-projector';
import type { IMultiProjectorStateCommon } from '../projectors/multi-projector-types';
import { AggregateListProjector } from '../queries/aggregate-list-projector';

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
  private queries: Map<string, any> = new Map();
  
  constructor(private registry: SchemaRegistry) {}
  
  registerQuery(name: string, queryClass: any): void {
    this.queries.set(name, queryClass);
  }

  getQueryTypes(): Array<QueryTypeInfo> {
    return Array.from(this.queries.entries()).map(([name, constructor]) => ({
      name,
      constructor
    }));
  }

  getQueryTypeByName(name: string): (new (...args: any[]) => any) | undefined {
    return this.queries.get(name);
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
 * Schema-based implementation of IMultiProjectorTypes
 */
class SchemaMultiProjectorTypes implements IMultiProjectorTypes {
  private multiProjectors: Map<string, IMultiProjectorCommon> = new Map();
  private aggregateListProjectors: Map<string, AggregateListProjector<any>> = new Map();
  
  constructor(private registry: SchemaRegistry) {}
  
  registerMultiProjector(name: string, projector: IMultiProjectorCommon): void {
    this.multiProjectors.set(name, projector);
  }
  
  registerAggregateListProjector<TProjector extends IAggregateProjector<any>>(
    projectorFactory: () => TProjector
  ): void {
    console.log('[SchemaMultiProjectorTypes] Starting AggregateListProjector registration');
    const projectorInstance = projectorFactory();
    console.log('[SchemaMultiProjectorTypes] Projector factory result:', {
      aggregateTypeName: projectorInstance.aggregateTypeName,
      projectorType: projectorInstance.constructor.name
    });
    
    const projector = AggregateListProjector.create(projectorFactory);
    const name = projector.getMultiProjectorName();
    
    console.log('[SchemaMultiProjectorTypes] Registration details:', {
      aggregateTypeNameFromProjector: projectorInstance.aggregateTypeName,
      generatedMultiProjectorName: name,
      projectorClass: projector.constructor.name
    });
    
    this.aggregateListProjectors.set(name, projector);
    console.log(`[SchemaMultiProjectorTypes] Registered AggregateListProjector with name: ${name}`);
  }
  
  project(multiProjector: IMultiProjectorCommon, event: IEvent): Result<IMultiProjectorCommon, SekibanError> {
    console.log('[SchemaMultiProjectorTypes.project] Called with:', {
      multiProjectorType: multiProjector.constructor.name,
      eventType: event.eventType,
      aggregateId: event.aggregateId,
      isAggregateListProjector: multiProjector instanceof AggregateListProjector
    });
    
    // For AggregateListProjector, we need to pass the projector itself as payload
    if (multiProjector instanceof AggregateListProjector) {
      const result = multiProjector.project(multiProjector, event);
      console.log('[SchemaMultiProjectorTypes.project] AggregateListProjector result:', {
        isOk: result.isOk(),
        error: result.isErr() ? result.error : null,
        resultAggregatesSize: result.isOk() ? (result.value as any).aggregates?.size : 'unknown'
      });
      return result;
    }
    // For other multi-projectors, they might have different signatures
    if ('project' in multiProjector && typeof multiProjector.project === 'function') {
      // This is a simplified approach - in reality, we'd need to track the payload separately
      const typedProjector = multiProjector as IMultiProjector<any>;
      return typedProjector.project(multiProjector as any, event);
    }
    return err(new EventStoreError('project', 'Invalid multi-projector type'));
  }
  
  projectEvents(multiProjector: IMultiProjectorCommon, events: readonly IEvent[]): Result<IMultiProjectorCommon, SekibanError> {
    let current = multiProjector;
    for (const event of events) {
      const result = this.project(current, event);
      if (result.isErr()) return result;
      current = result.value;
    }
    return ok(current);
  }
  
  getProjectorFromMultiProjectorName(grainName: string): IMultiProjectorCommon | undefined {
    return this.multiProjectors.get(grainName) || this.aggregateListProjectors.get(grainName);
  }
  
  getMultiProjectorNameFromMultiProjector(multiProjector: IMultiProjectorCommon): Result<string, SekibanError> {
    if ('getMultiProjectorName' in multiProjector && typeof multiProjector.getMultiProjectorName === 'function') {
      const typedProjector = multiProjector as IMultiProjector<any>;
      return ok(typedProjector.getMultiProjectorName());
    }
    return err(new EventStoreError('project', 'Invalid multi-projector type'));
  }
  
  toTypedState(state: IMultiProjectorStateCommon): IMultiProjectorStateCommon {
    // In TypeScript, we don't need runtime type conversion like C#
    return state;
  }
  
  getMultiProjectorTypes(): string[] {
    return [...this.multiProjectors.keys(), ...this.aggregateListProjectors.keys()];
  }
  
  generateInitialPayload(projectorType: string): Result<IMultiProjectorCommon, SekibanError> {
    const projector = this.getProjectorFromMultiProjectorName(projectorType);
    if (!projector) {
      return err(new EventStoreError('generateInitialPayload', `Unknown projector type: ${projectorType}`));
    }
    if ('generateInitialPayload' in projector && typeof projector.generateInitialPayload === 'function') {
      const typedProjector = projector as IMultiProjector<any>;
      return ok(typedProjector.generateInitialPayload());
    }
    return err(new EventStoreError('project', 'Invalid multi-projector type'));
  }
  
  async serializeMultiProjector(multiProjector: IMultiProjectorCommon): Promise<Result<string, SekibanError>> {
    try {
      // Special handling for AggregateListProjector to serialize Map properly
      if (multiProjector instanceof AggregateListProjector) {
        const aggregatesObj: Record<string, any> = {};
        const aggregatesMap = (multiProjector as any).aggregates;
        if (aggregatesMap instanceof Map) {
          aggregatesMap.forEach((value: any, key: string) => {
            aggregatesObj[key] = value;
          });
        }
        const serializable = {
          aggregates: aggregatesObj,
          // Include other relevant properties if needed
        };
        return ok(JSON.stringify(serializable));
      }
      
      return ok(JSON.stringify(multiProjector));
    } catch (error) {
      return err(new EventStoreError('serialize', `Failed to serialize multi-projector: ${error}`));
    }
  }
  
  async deserializeMultiProjector(json: string, typeFullName: string): Promise<Result<IMultiProjectorCommon, SekibanError>> {
    try {
      const data = JSON.parse(json);
      const templateProjector = this.getProjectorFromMultiProjectorName(typeFullName);
      if (!templateProjector) {
        return err(new EventStoreError('deserialize', `Unknown projector type: ${typeFullName}`));
      }
      
      // For AggregateListProjector, we need to reconstruct it properly
      if (typeFullName.startsWith('aggregatelistprojector-')) {
        const aggregateListProjector = this.aggregateListProjectors.get(typeFullName);
        if (aggregateListProjector && data.aggregates) {
          // Reconstruct the AggregateListProjector with the deserialized aggregates
          const aggregatesMap = new Map(Object.entries(data.aggregates)) as any;
          const projectorFactory = (aggregateListProjector as any).projectorFactory;
          return ok(new AggregateListProjector(aggregatesMap, projectorFactory));
        }
      }
      
      // For other projectors, attempt to reconstruct using constructor
      if ('generateInitialPayload' in templateProjector && typeof templateProjector.generateInitialPayload === 'function') {
        const typedProjector = templateProjector as IMultiProjector<any>;
        const initialPayload = typedProjector.generateInitialPayload();
        // Merge the deserialized data into the initial payload
        Object.assign(initialPayload, data);
        return ok(initialPayload);
      }
      
      return ok(data as IMultiProjectorCommon);
    } catch (error) {
      return err(new EventStoreError('deserialize', `Failed to deserialize multi-projector: ${error}`));
    }
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
    serializer: new SchemaSerializer(registry),
    multiProjectorTypes: new SchemaMultiProjectorTypes(registry)
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

/**
 * Export SchemaMultiProjectorTypes and SchemaQueryTypes for external use
 */
export { SchemaMultiProjectorTypes, SchemaQueryTypes };