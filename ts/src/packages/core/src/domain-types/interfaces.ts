import type { IEventPayload } from '../events/event-payload';
import type { ICommand } from '../commands/command';
import type { IAggregatePayload } from '../aggregates/aggregate-payload';
import type { IProjector } from '../aggregates/projector-interface';
import type { IEvent } from '../events/event';
import type { Result } from 'neverthrow';
import type { SekibanError } from '../result/errors';
import type { ICommandExecutor } from '../commands/executor';
import type { Metadata } from '../documents/metadata';
import type { IMultiProjectorTypes } from '../projectors/multi-projector-types';

// Type aliases for compatibility
export type EventTypeInfo = { name: string; constructor: any };
export type CommandTypeInfo = { name: string; constructor: any };
export type ProjectorTypeInfo = { name: string; constructor: any; aggregateTypeName: string };
export type AggregateTypeInfo = { name: string; constructor: any };
export type QueryTypeInfo = { name: string; constructor: any };

// Re-export IMultiProjectorTypes
export type { IMultiProjectorTypes } from '../projectors/multi-projector-types';

// Event document type
export interface EventDocument {
  id: string;
  eventType: string;
  aggregateId: string;
  aggregateType: string;
  rootPartitionKey?: string;
  payload: any;
  metadata: any;
  timestamp: string;
  version: number;
}

// Command metadata and result types
export type CommandMetadata = Partial<Metadata>;

export interface CommandResult {
  success: boolean;
  aggregateId: string;
  version: number;
  eventIds: any[];
}

/**
 * Interface for event type operations
 */
export interface IEventTypes {
  getEventTypes(): Array<{ name: string; constructor: any }>;
  getEventTypeByName(name: string): { name: string; constructor: any } | undefined;
  createEvent(type: string, payload: any): IEventPayload;
  serializeEvent(event: IEvent<IEventPayload>): any;
  deserializeEvent(data: any): Result<IEvent<IEventPayload>, SekibanError>;
}

/**
 * Interface for command type operations
 */
export interface ICommandTypes {
  getCommandTypes(): Array<{ name: string; constructor: any }>;
  getCommandTypeByName(name: string): { name: string; constructor: any } | undefined;
  createCommand(type: string, payload: any): ICommand<any>;
  executeCommand?(executor: ICommandExecutor, command: any, metadata: CommandMetadata): Promise<Result<CommandResult, SekibanError>>;
  getAggregateTypeForCommand?(commandType: string): string | undefined;
}

/**
 * Interface for projector type operations
 */
export interface IProjectorTypes {
  getProjectorTypes(): Array<{ aggregateTypeName: string; projector: IProjector<any> }>;
  getProjectorByAggregateType(aggregateType: string): IProjector<any> | undefined;
}

/**
 * Interface for query type operations
 */
export interface IQueryTypes {
  getQueryTypes(): Array<{ name: string; constructor: any }>;
  getQueryTypeByName(name: string): { name: string; constructor: any } | undefined;
}

/**
 * Interface for aggregate type operations
 */
export interface IAggregateTypes {
  getAggregateTypes(): Array<{ name: string; constructor: any }>;
  getAggregateTypeByName(name: string): { name: string; constructor: any } | undefined;
}

/**
 * Interface for serialization operations
 */
export interface ISekibanSerializer {
  serialize(obj: any): string;
  deserialize<T>(json: string): T;
}

/**
 * Central type registry interface that all executors must use
 */
export interface SekibanDomainTypes {
  readonly eventTypes: IEventTypes;
  readonly commandTypes: ICommandTypes;
  readonly projectorTypes: IProjectorTypes;
  readonly queryTypes: IQueryTypes;
  readonly aggregateTypes: IAggregateTypes;
  readonly serializer: ISekibanSerializer;
  readonly multiProjectorTypes?: IMultiProjectorTypes;
}