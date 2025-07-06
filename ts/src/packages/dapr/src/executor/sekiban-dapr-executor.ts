import type { Result } from 'neverthrow';
import { ok, err } from 'neverthrow';
import type { DaprClient } from '@dapr/dapr';
import { HttpMethod } from '@dapr/dapr';
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
import { PartitionKeysAndProjector } from '../parts/index.js';
import type { SekibanDomainTypes } from '@sekiban/core';

/**
 * Main Sekiban executor that uses Dapr for distributed aggregate management
 * Equivalent to C# SekibanDaprExecutor
 */
export class SekibanDaprExecutor implements ISekibanDaprExecutor {
  constructor(
    private readonly daprClient: DaprClient,
    private readonly domainTypes: SekibanDomainTypes,
    private configuration: DaprSekibanConfiguration
  ) {
    this.validateConfiguration(configuration);
  }
  
  private validateConfiguration(config: DaprSekibanConfiguration): void {
    // Configuration validation can be extended as needed
    if (!config.actorType) {
      config.actorType = 'AggregateActor';
    }
  }
  
  
  private createActorProxy(actorId: string): IDaprAggregateActorProxy {
    // Create a simple wrapper that uses HTTP client directly
    // The Dapr SDK's actor API seems to have changed
    const actorType = this.configuration.actorType;
    
    return {
      executeCommandAsync: async <TPayload extends ITypedAggregatePayload>(
        commandAndMetadata: SerializableCommandAndMetadata<TPayload>
      ): Promise<SekibanCommandResponse> => {
        // Use Dapr's HTTP API directly
        const response = await this.daprClient.invoker.invoke(
          this.configuration.actorIdPrefix || 'sekiban',
          `actors/${actorType}/${actorId}/method/executeCommandAsync`,
          HttpMethod.POST,
          commandAndMetadata
        );
        return response as SekibanCommandResponse;
      },
      
      queryAsync: async <T>(query: any): Promise<T> => {
        const response = await this.daprClient.invoker.invoke(
          this.configuration.actorIdPrefix || 'sekiban',
          `actors/${actorType}/${actorId}/method/queryAsync`,
          HttpMethod.POST,
          query
        );
        return response as T;
      },
      
      loadAggregateAsync: async <TPayload extends ITypedAggregatePayload>(
        partitionKeys: PartitionKeys
      ): Promise<Aggregate<TPayload>> => {
        const response = await this.daprClient.invoker.invoke(
          this.configuration.actorIdPrefix || 'sekiban',
          `actors/${actorType}/${actorId}/method/loadAggregateAsync`,
          HttpMethod.POST,
          partitionKeys
        );
        return response as Aggregate<TPayload>;
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
      
      // Get the projector for this aggregate type (using group as aggregate type name)
      const aggregateTypeName = partitionKeys.group || 'default';
      const projector = this.domainTypes.projectorTypes.getProjectorByAggregateType(aggregateTypeName);
      
      if (!projector) {
        return err({
          type: 'ProjectorNotFound',
          message: `No projector registered for aggregate type: ${aggregateTypeName}`
        } as SekibanError);
      }
      
      // Create PartitionKeysAndProjector (matching C# pattern)
      const partitionKeyAndProjector = new PartitionKeysAndProjector(partitionKeys, projector);
      
      // Generate actor ID using the projector grain key format
      const actorId = partitionKeyAndProjector.toProjectorGrainKey();
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
      // Verify the projector is registered
      const projectorConstructor = this.domainTypes.projectorTypes.getProjectorForAggregate(projector.aggregateTypeName);
      if (!projectorConstructor) {
        return err({
          type: 'ProjectorNotFound',
          message: `No projector registered for aggregate type: ${projector.aggregateTypeName}`
        } as SekibanError);
      }
      
      // Create PartitionKeysAndProjector (matching C# pattern)
      const partitionKeyAndProjector = new PartitionKeysAndProjector(partitionKeys, projector);
      
      // Generate actor ID using the projector grain key format
      const actorId = partitionKeyAndProjector.toProjectorGrainKey();
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
    // Get all projector types from domain types
    const projectorInfos = this.domainTypes.projectorTypes.getProjectorTypes();
    return projectorInfos.map(info => new info.constructor() as IAggregateProjector<any>);
  }
  
  getDomainTypes(): SekibanDomainTypes {
    return this.domainTypes;
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

export type { DaprSekibanConfiguration };