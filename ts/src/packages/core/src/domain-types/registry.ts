import type { IEventPayload } from '../events/event-payload.js';
import type { ICommand } from '../commands/command.js';
import type { IAggregatePayload } from '../aggregates/aggregate-payload.js';
import type { AggregateProjector } from '../aggregates/aggregate-projector.js';
import type { SekibanDomainTypes } from './interfaces.js';
import { EventTypesImpl } from './implementations/event-types-impl.js';
import { CommandTypesImpl } from './implementations/command-types-impl.js';
import { ProjectorTypesImpl } from './implementations/projector-types-impl.js';
import { AggregateTypesImpl } from './implementations/aggregate-types-impl.js';
import { QueryTypesImpl } from './implementations/query-types-impl.js';
import { DefaultSekibanSerializer } from './implementations/default-serializer.js';

export type EventConstructor = new (...args: any[]) => IEventPayload;
export type CommandConstructor = new (...args: any[]) => ICommand<IAggregatePayload>;
export type ProjectorConstructor = new (...args: any[]) => AggregateProjector<IAggregatePayload>;
export type AggregateConstructor = new (...args: any[]) => IAggregatePayload;
export type QueryConstructor = new (...args: any[]) => any;

/**
 * Global domain type registry for Sekiban.
 * This class manages registration of all domain types (events, commands, projectors, etc.)
 * and provides a factory method to create SekibanDomainTypes instances.
 */
class DomainTypeRegistry {
  private events = new Map<string, EventConstructor>();
  private commands = new Map<string, CommandConstructor>();
  private projectors = new Map<string, ProjectorConstructor>();
  private aggregates = new Map<string, AggregateConstructor>();
  private queries = new Map<string, QueryConstructor>();

  registerEvent(name: string, constructor: EventConstructor): void {
    if (this.events.has(name)) {
      console.warn(`Event type '${name}' is already registered. Overwriting...`);
    }
    this.events.set(name, constructor);
  }

  registerCommand(name: string, constructor: CommandConstructor): void {
    if (this.commands.has(name)) {
      console.warn(`Command type '${name}' is already registered. Overwriting...`);
    }
    this.commands.set(name, constructor);
  }

  registerProjector(name: string, constructor: ProjectorConstructor): void {
    if (this.projectors.has(name)) {
      console.warn(`Projector type '${name}' is already registered. Overwriting...`);
    }
    this.projectors.set(name, constructor);
  }

  registerAggregate(name: string, constructor: AggregateConstructor): void {
    if (this.aggregates.has(name)) {
      console.warn(`Aggregate type '${name}' is already registered. Overwriting...`);
    }
    this.aggregates.set(name, constructor);
  }

  registerQuery(name: string, constructor: QueryConstructor): void {
    if (this.queries.has(name)) {
      console.warn(`Query type '${name}' is already registered. Overwriting...`);
    }
    this.queries.set(name, constructor);
  }

  /**
   * Create a SekibanDomainTypes instance from the current registry state.
   * This is typically called once during application initialization.
   */
  createDomainTypes(): SekibanDomainTypes {
    return {
      eventTypes: new EventTypesImpl(new Map(this.events)),
      commandTypes: new CommandTypesImpl(new Map(this.commands)),
      projectorTypes: new ProjectorTypesImpl(new Map(this.projectors)),
      aggregateTypes: new AggregateTypesImpl(new Map(this.aggregates)),
      queryTypes: new QueryTypesImpl(new Map(this.queries)),
      serializer: new DefaultSekibanSerializer()
    };
  }

  /**
   * Clear all registrations. Useful for testing.
   */
  clear(): void {
    this.events.clear();
    this.commands.clear();
    this.projectors.clear();
    this.aggregates.clear();
    this.queries.clear();
  }

  /**
   * Get current registration counts for debugging.
   */
  getRegistrationCounts(): Record<string, number> {
    return {
      events: this.events.size,
      commands: this.commands.size,
      projectors: this.projectors.size,
      aggregates: this.aggregates.size,
      queries: this.queries.size
    };
  }
}

/**
 * Global singleton registry instance.
 * All decorators register types with this instance.
 */
export const GlobalRegistry = new DomainTypeRegistry();