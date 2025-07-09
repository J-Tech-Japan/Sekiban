import type { 
  SekibanDomainTypes,
  ICommandWithHandler,
  IAggregateProjector,
  ITypedAggregatePayload,
  EmptyAggregatePayload,
  PartitionKeys,
  Aggregate,
  Metadata,
  SekibanError
} from '@sekiban/core';
import { ok, err } from 'neverthrow';
import type { 
  SerializableCommandAndMetadata,
  SekibanCommandResponse,
  IDaprSerializationService,
  IActorProxyFactory
} from '../executor/interfaces.js';
import { PartitionKeysAndProjector } from '../parts/index.js';

/**
 * Domain implementation of AggregateActor with proper constructor injection
 * This class contains all the business logic and is testable
 */
export class AggregateActorImpl {
  private currentPartitionKeysAndProjector: PartitionKeysAndProjector | null = null;
  private hasUnsavedChanges: boolean = false;

  constructor(
    private readonly actorId: string,
    private readonly domainTypes: SekibanDomainTypes,
    private readonly serviceProvider: any,
    private readonly actorProxyFactory: IActorProxyFactory,
    private readonly serializationService: IDaprSerializationService,
    private readonly eventStore?: any
  ) {
    console.log(`[AggregateActorImpl] Created for actor ${actorId}`);
    console.log(`[AggregateActorImpl] Available command types:`, 
      this.domainTypes.commandTypes.getCommandTypes().map(c => c.name)
    );
  }

  /**
   * Initialize the actor (called from onActivate)
   */
  async initialize(): Promise<void> {
    console.log(`[AggregateActorImpl] Initializing actor ${this.actorId}`);
    // Any async initialization logic here
  }

  /**
   * Cleanup (called from onDeactivate)
   */
  async cleanup(): Promise<void> {
    console.log(`[AggregateActorImpl] Cleaning up actor ${this.actorId}`);
    if (this.hasUnsavedChanges) {
      await this.saveStateAsync();
    }
  }

  /**
   * Execute a command
   */
  async executeCommandAsync<
    TCommand,
    TProjector extends IAggregateProjector<TPayloadUnion>,
    TPayloadUnion extends ITypedAggregatePayload,
    TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
  >(
    commandAndMetadata: SerializableCommandAndMetadata<TCommand, TProjector, TPayloadUnion, TAggregatePayload>
  ): Promise<SekibanCommandResponse> {
    console.log(`[AggregateActorImpl] executeCommandAsync called`);
    console.log(`[AggregateActorImpl] Raw input:`, JSON.stringify(commandAndMetadata, null, 2));
    
    try {
      // Extract command information from the correct structure
      const commandType = commandAndMetadata.commandType;
      const commandData = commandAndMetadata.commandData;
      const partitionKeys = commandAndMetadata.partitionKeys;
      const metadata = commandAndMetadata.metadata;
      
      console.log('[AggregateActorImpl] Command type:', commandType);
      console.log('[AggregateActorImpl] Command data:', commandData);
      console.log('[AggregateActorImpl] Partition keys:', partitionKeys);
      console.log('[AggregateActorImpl] Metadata:', metadata);
      
      // Validate command exists
      const commandTypeDef = this.domainTypes.commandTypes.getCommandTypeByName(commandType);
      if (!commandTypeDef) {
        return {
          success: false,
          error: `Unknown command type: ${commandType}`,
          availableCommands: this.domainTypes.commandTypes.getCommandTypes().map(c => c.name)
        } as any;
      }

      // Store current partition keys for future use
      // For now, we'll create a simple response without full event sourcing logic
      console.log('[AggregateActorImpl] Command validated successfully');
      
      // For now, return a simplified response that matches SekibanCommandResponse
      // In a full implementation, this would:
      // 1. Load current aggregate state
      // 2. Execute the command handler
      // 3. Apply resulting events
      // 4. Save the updated state
      // 5. Publish events
      
      const response: SekibanCommandResponse = {
        aggregateId: this.actorId,
        lastSortableUniqueId: `mock-${Date.now()}`,
        success: true,
        metadata: {
          version: 1,
          commandType: commandType,
          processedAt: new Date().toISOString()
        }
      };
      
      console.log('[AggregateActorImpl] Returning response:', response);
      return response;
      
    } catch (error) {
      console.error('[AggregateActorImpl] Error in executeCommandAsync:', error);
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      } as any;
    }
  }

  /**
   * Get aggregate state
   */
  async getAggregateStateAsync<
    TPayload extends ITypedAggregatePayload = ITypedAggregatePayload
  >(): Promise<Aggregate<TPayload> | null> {
    console.log(`[AggregateActorImpl] getAggregateStateAsync called`);
    
    if (!this.currentPartitionKeysAndProjector) {
      return null;
    }

    // Return mock state for now
    return {
      id: this.actorId,
      version: 0,
      payload: {} as TPayload
    };
  }

  /**
   * Save state callback (for timer)
   */
  async saveStateCallbackAsync(): Promise<void> {
    if (this.hasUnsavedChanges) {
      await this.saveStateAsync();
    }
  }

  /**
   * Save state
   */
  async saveStateAsync(): Promise<void> {
    console.log(`[AggregateActorImpl] Saving state for actor ${this.actorId}`);
    this.hasUnsavedChanges = false;
    // Actual state saving logic would go here
  }

  /**
   * Rebuild state from events
   */
  async rebuildStateAsync(): Promise<void> {
    console.log(`[AggregateActorImpl] Rebuilding state for actor ${this.actorId}`);
    // State rebuilding logic would go here
  }

  /**
   * Get partition info
   */
  async getPartitionInfoAsync(): Promise<any> {
    return {
      actorId: this.actorId,
      partitionKeys: this.currentPartitionKeysAndProjector?.partitionKeys
    };
  }

  /**
   * Handle reminder
   */
  async receiveReminder(reminderName: string, state: any): Promise<void> {
    console.log(`[AggregateActorImpl] Received reminder ${reminderName}`);
    
    if (reminderName === 'SaveState') {
      await this.saveStateCallbackAsync();
    }
  }
}