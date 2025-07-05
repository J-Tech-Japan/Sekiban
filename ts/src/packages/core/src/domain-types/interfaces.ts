import type { IEventPayload } from '../events/event-payload.js';
import type { ICommand } from '../commands/command.js';
import type { IAggregatePayload } from '../aggregates/aggregate-payload.js';
import type { IProjector } from '../aggregates/aggregate-projector.js';
import type { IEvent } from '../events/event.js';
import type { Result } from 'neverthrow';
import type { SekibanError } from '../errors/sekiban-error.js';

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
}