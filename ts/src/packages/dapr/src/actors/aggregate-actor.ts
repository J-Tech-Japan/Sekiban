import { AbstractActor, type DaprClient, type ActorId } from '@dapr/dapr';
import { AggregateActorImpl } from './aggregate-actor-impl.js';
import type { 
  SerializableCommandAndMetadata,
  SekibanCommandResponse
} from '../executor/interfaces.js';
import type { IActorProxyFactory } from '../types/index.js';
import { getDaprCradle } from '../container/index.js';
import { globalRegistry } from '@sekiban/core';
import type { 
  Aggregate, 
  ITypedAggregatePayload,
  IAggregateProjector,
  EmptyAggregatePayload
} from '@sekiban/core';

/**
 * Thin wrapper actor that delegates to AggregateActorImpl
 * This follows the Wrapper Pattern for clean DI while maintaining Dapr compatibility
 * 
 * IMPORTANT: This class is named AggregateActor to match Dapr's expectations
 * The original AggregateActor has been renamed to AggregateActorLegacy
 */
export class AggregateActor extends AbstractActor {
  private impl!: AggregateActorImpl;
  private actorIdString!: string;
  
  // Explicitly define actor type for Dapr
  static get actorType() { 
    return "AggregateActor"; 
  }

  // Must keep this exact constructor signature for Dapr
  constructor(daprClient: DaprClient, id: ActorId) {
    try {
      super(daprClient, id);
      
      // Extract actor ID string
      this.actorIdString = (id as any).id || String(id);
      
      // Get dependencies from Awilix container
      const cradle = getDaprCradle();
      
      // Create implementation with proper DI
      this.impl = new AggregateActorImpl(
        this.actorIdString,
        cradle.domainTypes,
        cradle.serviceProvider || {},
        cradle.actorProxyFactory,
        cradle.eventStore
      );
    } catch (error) {
      throw error;
    }
  }

  async onActivate(): Promise<void> {
    await this.impl.initialize();
    
    // Register timer for periodic state saving
    // TODO: Fix timer registration - currently causing "method does not exist" error
    // await this.registerActorTimer(
    //   'SaveState',
    //   'saveStateCallbackAsync',
    //   null,
    //   '10s',  // Due time
    //   '10s'   // Period
    // );
  }

  async onDeactivate(): Promise<void> {
    await this.impl.cleanup();
  }

  // Delegate all actor methods to implementation

  async executeCommandAsync<
    TCommand,
    TProjector extends IAggregateProjector<TPayloadUnion>,
    TPayloadUnion extends ITypedAggregatePayload,
    TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
  >(
    commandAndMetadata: SerializableCommandAndMetadata<TCommand, TProjector, TPayloadUnion, TAggregatePayload>
  ): Promise<SekibanCommandResponse> {
    return this.impl.executeCommandAsync(commandAndMetadata);
  }

  async getAggregateStateAsync<TPayload extends ITypedAggregatePayload>(): Promise<Aggregate<TPayload> | null> {
    return this.impl.getAggregateStateAsync<TPayload>();
  }

  async saveStateCallbackAsync(): Promise<void> {
    return this.impl.saveStateCallbackAsync();
  }

  async saveStateAsync(): Promise<void> {
    return this.impl.saveStateAsync();
  }

  async rebuildStateAsync(): Promise<void> {
    return this.impl.rebuildStateAsync();
  }

  async receiveReminder(_data: string): Promise<void> {
    return this.impl.receiveReminder(_data, {});
  }

  async getPartitionInfoAsync(): Promise<any> {
    return this.impl.getPartitionInfoAsync();
  }

  // Additional methods required by the interface
  async queryAsync(query: any): Promise<any> {
    // For single-aggregate queries, return the current state
    const aggregateState = await this.getAggregateStateAsync();
    
    if (!aggregateState || !aggregateState.payload) {
      return null;
    }
    
    // Return the payload which contains the aggregate's current state
    return aggregateState.payload;
  }

  async loadAggregateAsync(partitionKeys: any): Promise<any> {
    // This delegates to getAggregateStateAsync
    return this.getAggregateStateAsync();
  }

  // Test method for debugging
  async testMethod(data: any): Promise<any> {
    return {
      success: true,
      actorId: this.actorIdString,
      message: 'Test method called via wrapper pattern',
      receivedData: data
    };
  }
}