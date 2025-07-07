// Temporary declaration file for @sekiban/core
// This will be removed once the package is published to npm
declare module '@sekiban/core' {
  import { z } from 'zod';
  import { Result as NeverthrowResult, ResultAsync as NeverthrowResultAsync } from 'neverthrow';

  // Schema Registry API
  export function defineEvent<T extends z.ZodSchema>(definition: {
    type: string;
    schema: T;
  }): EventDefinition<T>;

  export function defineCommand<T extends z.ZodSchema, TProjector, TPayload = unknown>(
    definition: CommandSchemaDefinition<T> | CommandSchemaDefinitionWithPayload<T, TProjector, TPayload>
  ): CommandDefinition<T>;

  export function defineProjector<TPayload>(definition: {
    aggregateType: string;
    initialPayload: TPayload;
    projections: Record<string, ProjectionFunction<TPayload>>;
  }): ProjectorDefinition<TPayload>;

  export function defineQuery<T extends z.ZodSchema>(definition: any): any;

  // Partition Keys
  export class PartitionKeys {
    readonly aggregateId: string;
    readonly group?: string;
    readonly rootPartitionKey?: string;
    static readonly DEFAULT_ROOT_PARTITION_KEY: string;
    static readonly DEFAULT_AGGREGATE_GROUP: string;
    readonly partitionKey: string;

    constructor(aggregateId: string, group?: string, rootPartitionKey?: string);
    static create(aggregateId: string, group?: string, rootPartitionKey?: string): PartitionKeys;
    static generate(group?: string, rootPartitionKey?: string): PartitionKeys;
    static existing(aggregateId: string, group?: string, rootPartitionKey?: string): PartitionKeys;
    toString(): string;
    equals(other: PartitionKeys): boolean;
    static toCompositeKey(keys: PartitionKeys): string;
    toPrimaryKeysString(): string;
    static fromPrimaryKeysString(primaryKeyString: string): PartitionKeys;
  }

  // Core Types
  export const EmptyAggregatePayload: {
    _tag: 'EmptyAggregatePayload';
  };

  export class SekibanError extends Error {
    constructor(message: string);
  }

  // Result handling (from neverthrow)
  export const ok: <T, E = never>(value: T) => NeverthrowResult<T, E>;
  export const err: <T = never, E = unknown>(error: E) => NeverthrowResult<T, E>;
  export const okAsync: <T, E = never>(value: T) => NeverthrowResultAsync<T, E>;
  export const errAsync: <T = never, E = unknown>(error: E) => NeverthrowResultAsync<T, E>;
  export type Result<T, E> = NeverthrowResult<T, E>;
  export type ResultAsync<T, E> = NeverthrowResultAsync<T, E>;

  // Registry and Domain Types
  export const globalRegistry: SchemaRegistry;
  export const createSchemaDomainTypes: (registry?: SchemaRegistry) => SekibanDomainTypes;
  export const SchemaDomainTypes: any;
  
  // Type definitions
  export type SekibanDomainTypes = {
    events: IEventTypes;
    commands: ICommandTypes;
    projectors: IProjectorTypes;
    queries: IQueryTypes;
    aggregates: IAggregateTypes;
    serializer: ISekibanSerializer;
  };

  export interface ISekibanExecutor {
    executeCommand<TCommand, TResult>(command: TCommand): ResultAsync<TResult, SekibanError>;
    executeQuery<TQuery, TResult>(query: TQuery): ResultAsync<TResult, SekibanError>;
  }

  export interface Event {
    type: string;
    payload: unknown;
    aggregateId: string;
    version: number;
    metadata?: Metadata;
  }

  export interface Aggregate {
    id: string;
    version: number;
    payload: unknown;
    lastSortableUniqueId?: string;
  }

  // Command context types
  export interface ICommandContextWithoutState {
    currentVersion: () => number;
    aggregateId: () => string;
    emit: <TEvent extends IEventPayload>(event: TEvent) => void;
  }

  export interface ICommandContext<TAggregatePayload> extends ICommandContextWithoutState {
    state: () => TAggregatePayload;
  }

  export interface ICommandWithHandler<TCommand, TProjector, TPayloadUnion = unknown, TAggregatePayload = TPayloadUnion> {
    handle(command: TCommand, context: ICommandContext<TAggregatePayload>): Result<void, SekibanError> | void;
  }

  // Supporting types
  export interface IEventPayload {
    type: string;
  }

  export interface Metadata {
    correlationId?: string;
    causationId?: string;
    executedUser?: string;
    [key: string]: unknown;
  }

  export interface IEventTypes {
    getEventType(eventType: string): unknown;
    serialize(event: unknown): string;
    deserialize(eventType: string, json: string): unknown;
  }

  export interface ICommandTypes {
    getCommandType(commandType: string): unknown;
    serialize(command: unknown): string;
    deserialize(commandType: string, json: string): unknown;
  }

  export interface IProjectorTypes {
    getProjectorType(projectorType: string): unknown;
  }

  export interface IQueryTypes {
    getQueryType(queryType: string): unknown;
    serialize(query: unknown): string;
    deserialize(queryType: string, json: string): unknown;
  }

  export interface IAggregateTypes {
    getAggregateType(aggregateType: string): unknown;
    serialize(aggregate: unknown): string;
    deserialize(aggregateType: string, json: string): unknown;
  }

  export interface ISekibanSerializer {
    serialize(value: unknown): string;
    deserialize<T>(json: string): T;
  }

  // Schema types
  export interface EventDefinition<T extends z.ZodSchema> {
    type: string;
    schema: T;
  }

  export interface CommandDefinition<T extends z.ZodSchema> {
    type: string;
    schema: T;
    projectorType?: string;
    handler?: unknown;
  }

  export interface ProjectorDefinition<TPayload> {
    aggregateType: string;
    initialPayload: TPayload;
    projections: Record<string, ProjectionFunction<TPayload>>;
  }

  export type ProjectionFunction<TPayload> = (
    payload: TPayload,
    event: unknown
  ) => TPayload;

  export interface SchemaRegistry {
    registerEvent<T extends EventDefinition<any>>(event: T): T;
    registerCommand<T extends CommandDefinition<any>>(command: T): T;
    registerProjector<T extends ProjectorDefinition<any>>(projector: T): T;
  }

  // Type inference helpers
  export type InferEventType<T extends EventDefinition<any>> = z.infer<T['schema']>;
  export type InferCommandType<T extends CommandDefinition<any>> = z.infer<T['schema']>;
  export type InferProjectorPayload<T extends ProjectorDefinition<any>> = T extends ProjectorDefinition<infer P> ? P : never;

  export type CommandSchemaDefinition<T extends z.ZodSchema> = {
    type: string;
    schema: T;
  };

  export type CommandSchemaDefinitionWithPayload<T extends z.ZodSchema, TProjector, TPayload> = {
    type: string;
    schema: T;
    projectorType: string;
    handler: (command: z.infer<T>, context: ICommandContext<TPayload>) => void | Result<void, SekibanError>;
  };
}