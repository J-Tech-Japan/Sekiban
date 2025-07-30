/**
 * Interface for AggregateActor
 * This interface is shared between services to enable type-safe actor communication
 */
export interface IAggregateActor {
  executeCommandAsync(data: any): Promise<any>;
  queryAsync(data: any): Promise<any>;
  loadAggregateAsync(data: any): Promise<any>;
  appendEventsAsync(expectedLastSortableUniqueId: string, events: any[]): Promise<any>;
}

/**
 * Class for ActorProxyBuilder type checking
 * This class is used only for type information, not for implementation
 */
export class AggregateActorBase implements IAggregateActor {
  async executeCommandAsync(data: any): Promise<any> {
    throw new Error('This is a type-only class, not for runtime use');
  }
  
  async queryAsync(data: any): Promise<any> {
    throw new Error('This is a type-only class, not for runtime use');
  }
  
  async loadAggregateAsync(data: any): Promise<any> {
    throw new Error('This is a type-only class, not for runtime use');
  }
  
  async appendEventsAsync(expectedLastSortableUniqueId: string, events: any[]): Promise<any> {
    throw new Error('This is a type-only class, not for runtime use');
  }
}