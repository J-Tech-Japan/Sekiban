import { ok, err, type Result } from 'neverthrow';
import type { IEventTypes, EventTypeInfo, EventDocument } from '../interfaces.js';
import type { IEvent } from '../../events/event.js';
import type { IEventPayload } from '../../events/event-payload.js';
import { SortableUniqueId } from '../../sortable-unique-id.js';

export class EventTypesImpl implements IEventTypes {
  constructor(
    private readonly events: Map<string, new (...args: any[]) => IEventPayload>
  ) {}

  getEventTypes(): Array<EventTypeInfo> {
    return Array.from(this.events.entries()).map(([name, constructor]) => ({
      name,
      constructor
    }));
  }

  getEventTypeByName(name: string): (new (...args: any[]) => IEventPayload) | undefined {
    return this.events.get(name);
  }

  createEvent<T extends IEventPayload>(name: string, payload: unknown): Result<T, Error> {
    const EventConstructor = this.events.get(name);
    if (!EventConstructor) {
      return err(new Error(`Event type '${name}' not found in registry`));
    }

    try {
      // Try to create instance directly with payload properties
      if (typeof payload === 'object' && payload !== null) {
        const instance = Object.assign(
          Object.create(EventConstructor.prototype),
          payload
        ) as T;
        return ok(instance);
      }

      // Fallback to constructor with spread
      const instance = new EventConstructor(...(Array.isArray(payload) ? payload : [payload])) as T;
      return ok(instance);
    } catch (error) {
      return err(new Error(
        `Failed to create event '${name}': ${error instanceof Error ? error.message : 'Unknown error'}`
      ));
    }
  }

  deserializeEvent(document: EventDocument): Result<IEvent, Error> {
    const payloadResult = this.createEvent(document.eventType, document.payload);
    if (payloadResult.isErr()) {
      return err(payloadResult.error);
    }

    const event: IEvent = {
      id: document.id,
      aggregateId: document.aggregateId,
      aggregateType: document.aggregateType,
      eventType: document.eventType,
      eventVersion: document.version.toString(),
      sortableUniqueId: SortableUniqueId.fromString(document.id),
      payload: payloadResult.value,
      createdBy: document.metadata.createdBy || 'system',
      createdAt: document.timestamp
    };

    return ok(event);
  }

  serializeEvent(event: IEvent): EventDocument {
    return {
      id: event.id,
      eventType: event.eventType,
      aggregateId: event.aggregateId,
      aggregateType: event.aggregateType,
      payload: JSON.parse(JSON.stringify(event.payload)), // Deep clone
      metadata: {
        createdBy: event.createdBy,
        correlationId: (event as any).correlationId
      },
      timestamp: event.createdAt,
      version: parseInt(event.eventVersion, 10)
    };
  }
}