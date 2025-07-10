import type { Result } from 'neverthrow';
import { ok, err } from 'neverthrow';
import type { DaprClient } from '@dapr/dapr';
import { HttpMethod, ActorProxyBuilder, ActorId } from '@dapr/dapr';
import type { 
  ICommandWithHandler,
  IAggregateProjector,
  ITypedAggregatePayload,
  EmptyAggregatePayload,
  PartitionKeys,
  Aggregate,
  Metadata
} from '@sekiban/core';
import { SekibanError } from '@sekiban/core';
import type { 
  DaprSekibanConfiguration,
  SekibanCommandResponse,
  ISekibanDaprExecutor,
  IDaprAggregateActorProxy,
  SerializableCommandAndMetadata
} from './interfaces.js';
import { PartitionKeysAndProjector } from '../parts/index.js';
import type { SekibanDomainTypes } from '@sekiban/core';
import { AggregateActorFactory } from '../actors/aggregate-actor-factory.js';

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
    // Define the interface for our aggregate actor
    interface AggregateActorInterface {
      executeCommandAsync(commandAndMetadata: any): Promise<SekibanCommandResponse>;
      queryAsync(query: any): Promise<any>;
      loadAggregateAsync(partitionKeys: any): Promise<any>;
      getAggregateStateAsync(): Promise<any>;
      saveStateAsync(): Promise<void>;
      rebuildStateAsync(): Promise<void>;
      getPartitionInfoAsync(): Promise<any>;
    }
    
    // Get the actual actor class from the factory
    const AggregateActorClass = AggregateActorFactory.createActorClass();
    
    // Create ActorProxyBuilder with the actual actor class
    const actorProxyBuilder = new ActorProxyBuilder<AggregateActorInterface>(
      AggregateActorClass, 
      this.daprClient
    );
    
    // Build the actor proxy with the actor ID
    const actor = actorProxyBuilder.build(new ActorId(actorId));
    
    // Return the proxy that implements IDaprAggregateActorProxy
    return {
      executeCommandAsync: async <
        TCommand,
        TProjector extends IAggregateProjector<TPayloadUnion>,
        TPayloadUnion extends ITypedAggregatePayload,
        TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
      >(
        commandAndMetadata: SerializableCommandAndMetadata<TCommand, TProjector, TPayloadUnion, TAggregatePayload>
      ): Promise<SekibanCommandResponse> => {
        console.log(`[SekibanDaprExecutor] Calling actor ${actorId} via ActorProxyBuilder`);
        return actor.executeCommandAsync(commandAndMetadata);
      },
      
      queryAsync: async <T>(query: any): Promise<T> => {
        return actor.queryAsync(query);
      },
      
      loadAggregateAsync: async <TPayload extends ITypedAggregatePayload>(
        partitionKeys: PartitionKeys
      ): Promise<Aggregate<TPayload>> => {
        return actor.loadAggregateAsync(partitionKeys);
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
   * Overloaded to accept either a command instance or command with data
   */
  async executeCommandAsync<
    TCommand,
    TProjector extends IAggregateProjector<TPayloadUnion>,
    TPayloadUnion extends ITypedAggregatePayload,
    TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
  >(
    commandOrInstance: ICommandWithHandler<TCommand, TProjector, TPayloadUnion, TAggregatePayload> | any,
    commandData?: TCommand,
    metadata?: Metadata
  ): Promise<Result<SekibanCommandResponse, SekibanError>> {
    // Handle case where command is passed as a single object (from defineCommand)
    let command: ICommandWithHandler<TCommand, TProjector, TPayloadUnion, TAggregatePayload>;
    let data: TCommand;
    
    if (commandData === undefined) {
      // Command instance from defineCommand
      command = commandOrInstance;
      // Extract data from the command instance
      // The command instance has a 'data' property
      data = (commandOrInstance as any).data || {} as TCommand;
    } else {
      // Separate command and data
      command = commandOrInstance as ICommandWithHandler<TCommand, TProjector, TPayloadUnion, TAggregatePayload>;
      data = commandData!;
    }
    try {
      // Validate command first
      const validationResult = command.validate(data);
      if (validationResult.isErr()) {
        return err(validationResult.error as SekibanError);
      }
      
      // Get partition keys for the command
      const partitionKeys = command.specifyPartitionKeys(data);
      
      // Get the projector from the command
      const projector = command.getProjector();
      
      // Create PartitionKeysAndProjector (matching C# pattern)
      const partitionKeyAndProjector = new PartitionKeysAndProjector(partitionKeys, projector);
      
      // Generate actor ID using the projector grain key format
      const actorId = partitionKeyAndProjector.toProjectorGrainKey();
      const actorProxy = this.createActorProxy(actorId);
      
      // Prepare command with metadata in SerializableCommandAndMetadata format (matches C#)
      const commandTypeName = command.commandType || (command as any).type || command.constructor.name;
      const projectorTypeName = projector.constructor.name;
      
      const commandWithMetadata: SerializableCommandAndMetadata<TCommand, TProjector, TPayloadUnion, TAggregatePayload> = {
        commandType: commandTypeName,
        commandData: data,
        partitionKeys: partitionKeys,
        metadata: {
          commandId: crypto.randomUUID(),
          causationId: metadata?.causationId || crypto.randomUUID(),
          correlationId: metadata?.correlationId || crypto.randomUUID(),
          executedUser: metadata?.executedUser || (metadata?.custom as any)?.user || 'system',
          projectorTypeName,
          aggregatePayloadTypeName: '', // Could be determined from projector if needed
          commandAssemblyVersion: '1.0.0'
        }
      };
      
      // Log what we're sending to the actor
      console.log('[SekibanDaprExecutor] Sending to actor:', JSON.stringify(commandWithMetadata, null, 2));
      
      // Execute command through actor with retry
      const responseData = await this.executeWithRetry(
        () => actorProxy.executeCommandAsync(commandWithMetadata),
        'Command execution'
      );
      
      // Handle response data - could be object or string
      let response: any;
      if (typeof responseData === 'string') {
        try {
          response = JSON.parse(responseData);
        } catch (parseError) {
          // If parsing fails, treat as error
          throw new Error(`Invalid JSON response: ${responseData}`);
        }
      } else if (responseData && typeof responseData === 'object') {
        // Already an object
        response = responseData;
      } else {
        // Handle other cases (null, undefined, etc.)
        throw new Error(`Unexpected response type: ${typeof responseData}`);
      }
      
      // Check if there's an error in the response
      if (response.error) {
        // Create a custom error class for command errors
        class CommandExecutionError extends SekibanError {
          readonly code = 'COMMAND_EXECUTION_ERROR';
        }
        return err(new CommandExecutionError(JSON.stringify(response.error)));
      }
      
      // Convert to SekibanCommandResponse format expected by the interface
      const commandResponse: SekibanCommandResponse = {
        aggregateId: response.aggregateId,
        lastSortableUniqueId: response.lastSortableUniqueId || '',
        success: true,
        metadata: {
          group: response.group,
          rootPartitionKey: response.rootPartitionKey,
          version: response.version,
          events: response.events
        }
      };
      
      return ok(commandResponse);
      
    } catch (error) {
      // Create a custom error class for Dapr actor errors
      class DaprActorError extends SekibanError {
        readonly code = 'DAPR_ACTOR_ERROR';
      }
      return err(new DaprActorError(error instanceof Error ? error.message : 'Unknown actor error'));
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
      // Create a custom error class for Dapr actor errors
      class DaprActorError extends SekibanError {
        readonly code = 'DAPR_ACTOR_ERROR';
      }
      return err(new DaprActorError(error instanceof Error ? error.message : 'Unknown actor error'));
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
      const projectorConstructor = this.domainTypes.projectorTypes.getProjectorByAggregateType(projector.aggregateTypeName);
      if (!projectorConstructor) {
        // Create a custom error class for projector not found
        class ProjectorNotFoundError extends SekibanError {
          readonly code = 'PROJECTOR_NOT_FOUND';
        }
        return err(new ProjectorNotFoundError(`No projector registered for aggregate type: ${projector.aggregateTypeName}`));
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
      // Create a custom error class for Dapr actor errors
      class DaprActorError extends SekibanError {
        readonly code = 'DAPR_ACTOR_ERROR';
      }
      return err(new DaprActorError(error instanceof Error ? error.message : 'Unknown actor error'));
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
    // The projector property contains the actual projector instance
    // We need to handle the type mismatch between IProjector and IAggregateProjector
    // In practice, the projector instances should implement IAggregateProjector
    return projectorInfos.map(info => info.projector as unknown as IAggregateProjector<any>);
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
  
  hasProjector(aggregateTypeName: string): boolean {
    const projectorConstructor = this.domainTypes.projectorTypes.getProjectorByAggregateType(aggregateTypeName);
    return projectorConstructor !== null && projectorConstructor !== undefined;
  }
}

export type { DaprSekibanConfiguration };