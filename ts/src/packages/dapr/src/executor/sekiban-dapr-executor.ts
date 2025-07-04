import type { Result } from 'neverthrow';
import { ok, err } from 'neverthrow';
import type { DaprClient } from '@dapr/dapr';
import type { 
  ICommand,
  IAggregateProjector,
  ITypedAggregatePayload,
  PartitionKeys,
  Aggregate,
  SekibanError
} from '@sekiban/core';
import type { 
  DaprSekibanConfiguration,
  SekibanCommandResponse,
  ISekibanDaprExecutor,
  IDaprAggregateActorProxy,
  SerializableCommandAndMetadata
} from './interfaces.js';

/**
 * Main Sekiban executor that uses Dapr for distributed aggregate management
 * Equivalent to C# SekibanDaprExecutor
 */
export class SekibanDaprExecutor implements ISekibanDaprExecutor {
  private projectorRegistry: Map<string, IAggregateProjector<any>> = new Map();
  
  constructor(
    private readonly daprClient: DaprClient,
    private configuration: DaprSekibanConfiguration
  ) {
    this.validateConfiguration(configuration);
    this.registerProjectors(configuration.projectors);
  }
  
  private validateConfiguration(config: DaprSekibanConfiguration): void {
    if (!config.projectors || config.projectors.length === 0) {
      throw new Error('At least one projector must be provided');
    }
  }
  
  private registerProjectors(projectors: IAggregateProjector<any>[]): void {
    for (const projector of projectors) {
      this.projectorRegistry.set(projector.aggregateTypeName, projector);
    }
  }
  
  private generateActorId(partitionKeys: PartitionKeys, aggregateTypeName: string): string {
    // Create actor ID using partition keys - similar to C# ActorId generation
    const prefix = this.configuration.actorIdPrefix || 'sekiban';
    return `${prefix}.${partitionKeys.rootPartitionKey}.${aggregateTypeName}.${partitionKeys.aggregateId}`;
  }
  
  private createActorProxy(actorId: string): IDaprAggregateActorProxy {
    // Create strongly-typed actor proxy
    const rawProxy = this.daprClient.actors.getActor(this.configuration.actorType, actorId);
    
    // Wrap with our interface for type safety
    return {
      executeCommandAsync: <TPayload extends ITypedAggregatePayload>(
        commandAndMetadata: SerializableCommandAndMetadata<TPayload>
      ): Promise<SekibanCommandResponse> => {
        return rawProxy.executeCommandAsync(commandAndMetadata);
      },
      
      queryAsync: <T>(query: any): Promise<T> => {
        return rawProxy.queryAsync(query);
      },
      
      loadAggregateAsync: <TPayload extends ITypedAggregatePayload>(
        partitionKeys: PartitionKeys
      ): Promise<Aggregate<TPayload>> => {
        return rawProxy.loadAggregateAsync(partitionKeys);
      }
    };
  }
  
  private async executeWithRetry<T>(
    operation: () => Promise<T>,
    operationName: string
  ): Promise<T> {
    const maxRetries = this.configuration.retryAttempts || 3;
    let lastError: Error;
    
    for (let attempt = 1; attempt <= maxRetries; attempt++) {
      try {
        return await operation();
      } catch (error) {
        lastError = error instanceof Error ? error : new Error('Unknown error');
        
        if (attempt === maxRetries) {
          throw new Error(`${operationName} failed after ${maxRetries} attempts: ${lastError.message}`);
        }
        
        // Wait before retry (exponential backoff)
        await new Promise(resolve => setTimeout(resolve, Math.pow(2, attempt) * 100));
      }
    }
    
    throw lastError!;
  }
  
  /**
   * Execute command through Dapr AggregateActor
   */
  async executeCommandAsync<TPayload extends ITypedAggregatePayload>(
    command: ICommand<TPayload>
  ): Promise<Result<SekibanCommandResponse, SekibanError>> {
    try {
      // Validate command first
      const validationResult = command.validate();
      if (validationResult.isErr()) {
        return err(validationResult.error as SekibanError);
      }
      
      // Get partition keys for the command
      const partitionKeys = command.specifyPartitionKeys();
      
      // Verify we have a projector for this aggregate type
      if (!this.hasProjector(partitionKeys.partitionKey)) {
        return err({
          type: 'ProjectorNotFound',
          message: `No projector registered for aggregate type: ${partitionKeys.partitionKey}`
        } as SekibanError);
      }
      
      // Generate actor ID and create proxy
      const actorId = this.generateActorId(partitionKeys, partitionKeys.partitionKey);
      const actorProxy = this.createActorProxy(actorId);
      
      // Prepare command with metadata
      const commandWithMetadata: SerializableCommandAndMetadata<TPayload> = {
        command,
        partitionKeys,
        metadata: {
          timestamp: new Date().toISOString(),
          requestId: crypto.randomUUID()
        }
      };
      
      // Execute command through actor with retry
      const response = await this.executeWithRetry(
        () => actorProxy.executeCommandAsync(commandWithMetadata),
        'Command execution'
      );
      
      return ok(response);
      
    } catch (error) {
      return err({
        type: 'DaprActorError',
        message: error instanceof Error ? error.message : 'Unknown actor error'
      } as SekibanError);
    }
  }
  
  /**
   * Execute query through Dapr actor
   */
  async queryAsync<T>(query: any): Promise<Result<T, SekibanError>> {
    try {
      // For queries, determine appropriate actor ID
      // This is a simplified approach - real implementation would be more sophisticated
      const actorId = `${this.configuration.actorIdPrefix || 'sekiban'}.query.${query.queryType}.${query.userId || 'default'}`;
      
      // Create actor proxy
      const actorProxy = this.createActorProxy(actorId);
      
      // Execute query through actor with retry
      const response = await this.executeWithRetry(
        () => actorProxy.queryAsync<T>(query),
        'Query execution'
      );
      
      return ok(response);
      
    } catch (error) {
      return err({
        type: 'DaprActorError',
        message: error instanceof Error ? error.message : 'Unknown actor error'
      } as SekibanError);
    }
  }
  
  /**
   * Load aggregate from Dapr actor
   */
  async loadAggregateAsync<TPayload extends ITypedAggregatePayload>(
    projector: IAggregateProjector<TPayload>,
    partitionKeys: PartitionKeys
  ): Promise<Result<Aggregate<TPayload>, SekibanError>> {
    try {
      // Verify we have a projector for this aggregate type
      if (!this.hasProjector(projector.aggregateTypeName)) {
        return err({
          type: 'ProjectorNotFound',
          message: `No projector registered for aggregate type: ${projector.aggregateTypeName}`
        } as SekibanError);
      }
      
      // Generate actor ID and create proxy
      const actorId = this.generateActorId(partitionKeys, projector.aggregateTypeName);
      const actorProxy = this.createActorProxy(actorId);
      
      // Load aggregate through actor with retry
      const response = await this.executeWithRetry(
        () => actorProxy.loadAggregateAsync<TPayload>(partitionKeys),
        'Aggregate loading'
      );
      
      return ok(response);
      
    } catch (error) {
      return err({
        type: 'DaprActorError',
        message: error instanceof Error ? error.message : 'Unknown actor error'
      } as SekibanError);
    }
  }
  
  // Getters for configuration and state
  getDaprClient(): DaprClient {
    return this.daprClient;
  }
  
  getConfiguration(): DaprSekibanConfiguration {
    return { ...this.configuration };
  }
  
  getRegisteredProjectors(): IAggregateProjector<any>[] {
    return Array.from(this.projectorRegistry.values());
  }
  
  hasProjector(aggregateTypeName: string): boolean {
    return this.projectorRegistry.has(aggregateTypeName);
  }
  
  getStateStoreName(): string {
    return this.configuration.stateStoreName;
  }
  
  getPubSubName(): string {
    return this.configuration.pubSubName;
  }
  
  getEventTopicName(): string {
    return this.configuration.eventTopicName;
  }
  
  updateConfiguration(updates: Partial<DaprSekibanConfiguration>): void {
    this.configuration = { ...this.configuration, ...updates };
  }
}