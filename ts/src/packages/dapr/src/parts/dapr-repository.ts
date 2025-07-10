import { Result, ok, err, ResultAsync } from 'neverthrow';
import {
  Aggregate,
  IEvent,
  PartitionKeys,
  AggregateProjector,
  EventStoreError,
  SortableUniqueId,
  EmptyAggregatePayload
} from '@sekiban/core';
import type { IAggregateEventHandlerActor, SerializableEventDocument } from '../actors/interfaces.js';
import type { SekibanDomainTypes } from '@sekiban/core';

/**
 * Repository implementation for Dapr actors, bridging between AggregateActor and AggregateEventHandlerActor.
 * This is the Dapr equivalent of Orleans' OrleansRepository and C#'s DaprRepository.
 */
export class DaprRepository {
  private currentAggregate: Aggregate;

  constructor(
    private readonly eventHandlerActor: IAggregateEventHandlerActor,
    private readonly partitionKeys: PartitionKeys,
    private readonly projector: AggregateProjector<any>,
    private readonly domainTypes: SekibanDomainTypes,
    currentAggregate: Aggregate
  ) {
    this.currentAggregate = currentAggregate;
  }

  getAggregate(): Result<Aggregate, never> {
    return ok(this.currentAggregate);
  }

  async save(
    lastSortableUniqueId: string,
    newEvents: IEvent[]
  ): Promise<Result<IEvent[], SekibanError>> {
    if (!newEvents || newEvents.length === 0) {
      return ok([]);
    }

    try {
      // Convert events to serializable documents using domain types
      const eventDocuments: SerializableEventDocument[] = newEvents.map(event => {
        // Use domain types to serialize the event
        const eventDocument = this.domainTypes.eventTypes.serializeEvent(event);
        
        return {
          id: event.id.toString(),
          sortableUniqueId: event.id.toString(),
          payload: eventDocument.payload,
          eventType: eventDocument.eventType,
          aggregateId: eventDocument.aggregateId,
          partitionKeys: event.partitionKeys,
          version: eventDocument.version,
          createdAt: eventDocument.timestamp.toISOString(),
          metadata: eventDocument.metadata
        };
      });

      // Call the event handler actor to append events
      const response = await this.eventHandlerActor.appendEventsAsync(
        lastSortableUniqueId,
        eventDocuments
      );

      if (!response.isSuccess) {
        return err(new EventStoreError(response.error || 'Failed to append events'));
      }

      // Note: Unlike the previous implementation, we should NOT update currentAggregate here
      // The caller (AggregateActor) will handle the aggregate update via getProjectedAggregate
      console.log(`[DaprRepository.save] Events saved: ${newEvents.length}, current version: ${this.currentAggregate.version}`);

      return ok(newEvents);
    } catch (error) {
      return err(new EventStoreError(error instanceof Error ? error.message : 'Unknown error'));
    }
  }

  async load(): Promise<Result<Aggregate, SekibanError>> {
    try {
      // Get all events from the event handler
      const eventDocuments = await this.eventHandlerActor.getAllEventsAsync();

      // Convert documents back to events using domain types for proper deserialization
      const events: IEvent[] = [];
      for (const doc of eventDocuments) {
        const eventDocument = {
          id: doc.id,
          eventType: doc.eventType,
          aggregateId: doc.aggregateId,
          aggregateType: doc.partitionKeys.group || '',
          payload: doc.payload,
          metadata: doc.metadata || {},
          timestamp: new Date(doc.createdAt),
          version: doc.version
        };
        
        const deserializedResult = this.domainTypes.eventTypes.deserializeEvent(eventDocument);
        if (deserializedResult.isErr()) {
          throw deserializedResult.error;
        }
        events.push(deserializedResult.value);
      }

      // Start with empty aggregate
      let aggregate = Aggregate.emptyFromPartitionKeys(this.partitionKeys);

      // Project all events
      for (const event of events) {
        const projectResult = this.projector.project(aggregate, event);
        if (projectResult.isErr()) {
          return err(projectResult.error);
        }
        aggregate = projectResult.value;
      }

      // Update current aggregate
      this.currentAggregate = aggregate;

      return ok(aggregate);
    } catch (error) {
      return err(new EventStoreError(error instanceof Error ? error.message : 'Unknown error'));
    }
  }

  getProjectedAggregate(projectedEvents: IEvent[]): Result<Aggregate, SekibanError> {
    try {
      console.log(`[getProjectedAggregate] Current version: ${this.currentAggregate.version}, projecting ${projectedEvents.length} events`);

      // Project the events onto the current aggregate to get a new aggregate
      let aggregate = this.currentAggregate;
      for (const event of projectedEvents) {
        const projectResult = this.projector.project(aggregate, event);
        if (projectResult.isErr()) {
          return err(projectResult.error);
        }
        aggregate = projectResult.value;
      }

      console.log(`[getProjectedAggregate] After projection - version: ${aggregate.version}`);
      return ok(aggregate);
    } catch (error) {
      return err(new EventStoreError(error instanceof Error ? error.message : 'Unknown error'));
    }
  }
}