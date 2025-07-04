import type { IEvent } from '../events/event.js';
import type { IEventPayload } from '../events/event-payload.js';
import type { ICommand } from '../commands/command.js';
import type { IAggregatePayload } from '../aggregates/aggregate-payload.js';
import type { AggregateProjector } from '../aggregates/aggregate-projector.js';
import type { CommandExecutor } from '../executors/command-executor.js';
import type { Result } from 'neverthrow';
import type { SekibanError } from '../errors/sekiban-error.js';

// Type information structures
export interface EventTypeInfo {
  name: string;
  constructor: new (...args: any[]) => IEventPayload;
}

export interface CommandTypeInfo {
  name: string;
  constructor: new (...args: any[]) => ICommand<IAggregatePayload>;
}

export interface ProjectorTypeInfo {
  name: string;
  constructor: new (...args: any[]) => AggregateProjector<IAggregatePayload>;
  aggregateTypeName: string;
}

export interface AggregateTypeInfo {
  name: string;
  constructor: new (...args: any[]) => IAggregatePayload;
}

export interface QueryTypeInfo {
  name: string;
  constructor: new (...args: any[]) => any;
}

// Event document for serialization
export interface EventDocument {
  id: string;
  eventType: string;
  aggregateId: string;
  aggregateType: string;
  payload: Record<string, any>;
  metadata: Record<string, any>;
  timestamp: Date;
  version: number;
}

// Command metadata
export interface CommandMetadata {
  commandId: string;
  userId?: string;
  correlationId?: string;
  timestamp: Date;
}

// Command result
export interface CommandResult {
  success: boolean;
  events: IEvent[];
  aggregateId: string;
  version: number;
  error?: SekibanError;
}

// Registry interfaces
export interface IEventTypes {
  getEventTypes(): Array<EventTypeInfo>;
  getEventTypeByName(name: string): (new (...args: any[]) => IEventPayload) | undefined;
  createEvent<T extends IEventPayload>(name: string, payload: unknown): Result<T, Error>;
  deserializeEvent(document: EventDocument): Result<IEvent, Error>;
  serializeEvent(event: IEvent): EventDocument;
}

export interface ICommandTypes {
  getCommandTypes(): Array<CommandTypeInfo>;
  getCommandTypeByName(name: string): (new (...args: any[]) => ICommand<IAggregatePayload>) | undefined;
  executeCommand(
    executor: CommandExecutor, 
    command: unknown, 
    metadata: CommandMetadata
  ): Promise<Result<CommandResult, SekibanError>>;
}

export interface IProjectorTypes {
  getProjectorTypes(): Array<ProjectorTypeInfo>;
  getProjectorByName(name: string): (new (...args: any[]) => AggregateProjector<IAggregatePayload>) | undefined;
  getProjectorForAggregate(aggregateType: string): (new (...args: any[]) => AggregateProjector<IAggregatePayload>) | undefined;
}

export interface IAggregateTypes {
  getAggregateTypes(): Array<AggregateTypeInfo>;
  getAggregateTypeByName(name: string): (new (...args: any[]) => IAggregatePayload) | undefined;
}

export interface IQueryTypes {
  getQueryTypes(): Array<QueryTypeInfo>;
  getQueryTypeByName(name: string): (new (...args: any[]) => any) | undefined;
}

// Serializer interface
export interface ISekibanSerializer {
  serialize(value: any): string;
  deserialize<T>(json: string, type?: new (...args: any[]) => T): T;
}

// Main domain types interface
export interface SekibanDomainTypes {
  readonly eventTypes: IEventTypes;
  readonly commandTypes: ICommandTypes;
  readonly projectorTypes: IProjectorTypes;
  readonly queryTypes: IQueryTypes;
  readonly aggregateTypes: IAggregateTypes;
  readonly serializer: ISekibanSerializer;
}