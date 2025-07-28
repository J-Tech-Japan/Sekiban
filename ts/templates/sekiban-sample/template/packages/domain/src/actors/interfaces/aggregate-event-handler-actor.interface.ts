/**
 * Response type for event handling operations
 */
export interface EventHandlingResponse {
  isSuccess: boolean;
  lastSortableUniqueId?: string;
  error?: string;
}

/**
 * Interface for AggregateEventHandlerActor
 * This interface is shared between services to enable type-safe actor communication
 */
export interface IAggregateEventHandlerActor {
  appendEventsAsync(expectedLastSortableUniqueId: string, events: any[]): Promise<EventHandlingResponse>;
  getAllEventsAsync(): Promise<any[]>;
}

/**
 * Class for ActorProxyBuilder type checking
 * This class is used only for type information, not for implementation
 */
export class AggregateEventHandlerActorBase implements IAggregateEventHandlerActor {
  async appendEventsAsync(expectedLastSortableUniqueId: string, events: any[]): Promise<EventHandlingResponse> {
    throw new Error('This is a type-only class, not for runtime use');
  }
  
  async getAllEventsAsync(): Promise<any[]> {
    throw new Error('This is a type-only class, not for runtime use');
  }
}