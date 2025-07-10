import { AbstractActor, type DaprClient, type ActorId } from '@dapr/dapr';
import { AggregateActorImpl } from './aggregate-actor-impl.js';
import type { 
  SerializableCommandAndMetadata,
  SekibanCommandResponse
} from '../executor/interfaces.js';
import type { IActorProxyFactory } from '../types/index.js';
import { getDaprCradle } from '../container/index.js';
import type { Aggregate, ITypedAggregatePayload } from '@sekiban/core';

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

  // Must keep this exact constructor signature for Dapr
  constructor(daprClient: DaprClient, id: ActorId) {
    try {
      super(daprClient, id);
      console.log('[AggregateActor] Constructor called');
      
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
      
      console.log('[AggregateActor] Implementation created');
    } catch (error) {
      console.error('[AggregateActor] Constructor error:', error);
      console.error('[AggregateActor] Stack trace:', (error as Error).stack);
      throw error;
    }
  }

  async onActivate(): Promise<void> {
    console.log(`[AggregateActor] onActivate called for ${this.actorIdString}`);
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
    console.log(`[AggregateActor] onDeactivate called for ${this.actorIdString}`);
    await this.impl.cleanup();
  }

  // Delegate all actor methods to implementation

  async executeCommandAsync<
    TCommand,
    TProjector,
    TPayloadUnion,
    TAggregatePayload
  >(
    commandAndMetadata: SerializableCommandAndMetadata<TCommand, TProjector, TPayloadUnion, TAggregatePayload>
  ): Promise<SekibanCommandResponse> {
    console.log(`[AggregateActorWrapper] executeCommandAsync called with actor ID: ${this.getActorId()?.toString()}`);
    console.log(`[AggregateActorWrapper] Command type: ${commandAndMetadata?.commandType}`);
    console.log(`[AggregateActorWrapper] Delegating to implementation...`);
    
    try {
      const result = await this.impl.executeCommandAsync(commandAndMetadata);
      console.log(`[AggregateActorWrapper] executeCommandAsync completed with result:`, result);
      return result;
    } catch (error) {
      console.error(`[AggregateActorWrapper] executeCommandAsync error:`, error);
      throw error;
    }
  }

  async getAggregateStateAsync<TPayload extends ITypedAggregatePayload>(): Promise<Aggregate<TPayload> | null> {
    console.log(`[AggregateActorWrapper] Delegating getAggregateStateAsync`);
    return this.impl.getAggregateStateAsync<TPayload>();
  }

  async saveStateCallbackAsync(): Promise<void> {
    console.log(`[AggregateActorWrapper] Delegating saveStateCallbackAsync`);
    return this.impl.saveStateCallbackAsync();
  }

  async saveStateAsync(): Promise<void> {
    console.log(`[AggregateActorWrapper] Delegating saveStateAsync`);
    return this.impl.saveStateAsync();
  }

  async rebuildStateAsync(): Promise<void> {
    console.log(`[AggregateActorWrapper] Delegating rebuildStateAsync`);
    return this.impl.rebuildStateAsync();
  }

  async receiveReminder(_data: string): Promise<void> {
    console.log(`[AggregateActorWrapper] Delegating receiveReminder: ${_data}`);
    return this.impl.receiveReminder(_data, {});
  }

  async getPartitionInfoAsync(): Promise<any> {
    console.log(`[AggregateActorWrapper] Delegating getPartitionInfoAsync`);
    return this.impl.getPartitionInfoAsync();
  }

  // Test method for debugging
  async testMethod(data: any): Promise<any> {
    console.log(`[AggregateActorWrapper] testMethod called with:`, data);
    return {
      success: true,
      actorId: this.actorIdString,
      message: 'Test method called via wrapper pattern',
      receivedData: data
    };
  }
}