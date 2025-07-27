import type { Result } from 'neverthrow';
import { ok, err } from 'neverthrow';
import { DaprClient, HttpMethod, ActorProxyBuilder, ActorId, CommunicationProtocolEnum } from '@dapr/dapr';
import type { 
  ICommandWithHandler,
  IAggregateProjector,
  ITypedAggregatePayload,
  EmptyAggregatePayload,
  PartitionKeys,
  Aggregate,
  Metadata
} from '@sekiban/core';
import { SekibanError, QueryExecutionError } from '@sekiban/core';
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
import { MultiProjectorActorFactory } from '../actors/multi-projector-actor-factory.js';
import type { SerializableQuery, SerializableListQuery, QueryResponse, ListQueryResponse, IMultiProjectorActor, ActorMultiProjectionState, DaprEventEnvelope, SerializableQueryResult, SerializableListQueryResult } from '../actors/interfaces.js';
import { deserializeListQueryResult } from '../actors/serializable-query-results.js';

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
          causationId: metadata?.causationId || crypto.randomUUID(),
          correlationId: metadata?.correlationId || crypto.randomUUID(),
          executedUser: metadata?.executedUser || (metadata?.custom as any)?.user || 'system',
          timestamp: metadata?.timestamp || new Date(),
          custom: {
            commandId: crypto.randomUUID(),
            projectorTypeName,
            aggregatePayloadTypeName: '', // Could be determined from projector if needed
            commandAssemblyVersion: '1.0.0',
            ...metadata?.custom
          }
        }
      };
      
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
          constructor(message: string) {
            super(message);
          }
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
        constructor(message: string) {
          super(message);
        }
      }
      return err(new DaprActorError(error instanceof Error ? error.message : 'Unknown actor error'));
    }
  }
  
  /**
   * Check if a query is a multi-projection query
   */
  private isMultiProjectionQuery(query: any): boolean {
    console.log('[SekibanDaprExecutor] Checking if multi-projection query:', {
      hasGetPartitionKeys: !!query.getPartitionKeys,
      partitionKeys: query.getPartitionKeys ? query.getPartitionKeys() : 'N/A',
      isMultiProjection: query.isMultiProjection,
      queryType: query.constructor.name
    });
    
    // Check if query has no partition keys or explicitly marked as multi-projection
    const result = !query.getPartitionKeys || !query.getPartitionKeys() || query.isMultiProjection === true;
    console.log('[SekibanDaprExecutor] Is multi-projection:', result);
    return result;
  }
  
  /**
   * Get the multi-projector name for a query
   */
  private getMultiProjectorName(query: any): string | null {
    console.log('[SekibanDaprExecutor.getMultiProjectorName] Checking query:', {
      queryClassName: query.constructor.name,
      hasGetMultiProjectorName: !!query.getMultiProjectorName,
      hasGetProjector: !!query.getProjector,
      hasGetAggregateType: !!query.getAggregateType
    });
    
    // Check if query has a getMultiProjectorName method
    if (query.getMultiProjectorName && typeof query.getMultiProjectorName === 'function') {
      const multiProjectorName = query.getMultiProjectorName();
      console.log('[SekibanDaprExecutor.getMultiProjectorName] From query.getMultiProjectorName():', multiProjectorName);
      return multiProjectorName;
    }
    
    // If query has a getProjector method and the projector has getMultiProjectorName
    if (query.getProjector && typeof query.getProjector === 'function') {
      const projector = query.getProjector();
      console.log('[SekibanDaprExecutor.getMultiProjectorName] Got projector from query:', {
        projectorExists: !!projector,
        projectorType: projector?.constructor?.name,
        hasGetMultiProjectorName: projector && projector.getMultiProjectorName && typeof projector.getMultiProjectorName === 'function'
      });
      
      if (projector && projector.getMultiProjectorName && typeof projector.getMultiProjectorName === 'function') {
        const multiProjectorName = projector.getMultiProjectorName();
        console.log('[SekibanDaprExecutor.getMultiProjectorName] From projector.getMultiProjectorName():', multiProjectorName);
        return multiProjectorName;
      }
    }
    
    // Fallback: use aggregate type as projector name
    if (query.getAggregateType && typeof query.getAggregateType === 'function') {
      const aggregateType = query.getAggregateType();
      const multiProjectorName = `${aggregateType}MultiProjector`;
      console.log('[SekibanDaprExecutor.getMultiProjectorName] Fallback from aggregate type:', {
        aggregateType,
        generatedName: multiProjectorName
      });
      return multiProjectorName;
    }
    
    console.log('[SekibanDaprExecutor.getMultiProjectorName] No multi-projector name found');
    return null;
  }
  
  /**
   * Create multi-projector actor proxy with HTTP-based implementation
   */
  private createMultiProjectorActorProxy(projectorName: string): IMultiProjectorActor {
    console.log(`[SekibanDaprExecutor] Creating MultiProjectorActor proxy for: ${projectorName}`);
    
    const targetAppId = this.configuration.multiProjectorAppId || 'dapr-sample-multi-projector';
    const actorType = 'MultiProjectorActor';
    const daprPort = this.daprClient.options.daprPort || '3500';
    const daprHost = this.daprClient.options.daprHost || '127.0.0.1';
    
    console.log(`[SekibanDaprExecutor] Target app-id: ${targetAppId}, Actor type: ${actorType}`);
    
    // Create a custom proxy that uses HTTP API directly
    const proxy: IMultiProjectorActor = {
      queryAsync: async (query: SerializableQuery): Promise<QueryResponse> => {
        const url = `http://${daprHost}:${daprPort}/v1.0/actors/${actorType}/${projectorName}/method/queryAsync`;
        console.log(`[SekibanDaprExecutor] Calling actor method at: ${url}`);
        
        try {
          const response = await fetch(url, {
            method: 'PUT',
            headers: {
              'Content-Type': 'application/json',
              'dapr-app-id': targetAppId
            },
            body: JSON.stringify(query)
          });
          
          if (!response.ok) {
            const errorText = await response.text();
            console.error(`[SekibanDaprExecutor] Actor call failed: ${response.status} ${errorText}`);
            return {
              isSuccess: false,
              error: `Actor call failed: ${response.status} ${errorText}`
            };
          }
          
          const result = await response.json();
          console.log(`[SekibanDaprExecutor] Actor response:`, result);
          return result as QueryResponse;
        } catch (error) {
          console.error(`[SekibanDaprExecutor] Network error calling actor:`, error);
          return {
            isSuccess: false,
            error: error instanceof Error ? error.message : 'Unknown error'
          };
        }
      },
      
      queryListAsync: async (query: SerializableListQuery): Promise<ListQueryResponse> => {
        const url = `http://${daprHost}:${daprPort}/v1.0/actors/${actorType}/${projectorName}/method/queryListAsync`;
        console.log(`[SekibanDaprExecutor] Calling list actor method at: ${url}`);
        
        try {
          const response = await fetch(url, {
            method: 'PUT',
            headers: {
              'Content-Type': 'application/json',
              'dapr-app-id': targetAppId
            },
            body: JSON.stringify(query)
          });
          
          if (!response.ok) {
            const errorText = await response.text();
            console.error(`[SekibanDaprExecutor] Actor list call failed: ${response.status} ${errorText}`);
            return {
              isSuccess: false,
              error: `Actor call failed: ${response.status} ${errorText}`
            };
          }
          
          const result = await response.json();
          console.log(`[SekibanDaprExecutor] Actor list response:`, result);
          
          // The actor returns SerializableListQueryResult, we need to convert it
          // For now, check if it's the expected format or needs conversion
          if (result && typeof result === 'object') {
            // Check if the result has a data property with compressed items
            if ('isSuccess' in result && 'data' in result && result.data && 
                'compressedItemsJson' in result.data && result.data.compressedItemsJson) {
              console.log(`[SekibanDaprExecutor] Deserializing compressed query result from data property`);
              const deserializeResult = await deserializeListQueryResult(
                result.data as any,
                this.domainTypes
              );
              
              if (deserializeResult.isErr()) {
                console.error(`[SekibanDaprExecutor] Failed to deserialize:`, deserializeResult.error);
                return {
                  isSuccess: false,
                  error: deserializeResult.error.message
                };
              }
              
              const listResult = deserializeResult.value;
              return {
                isSuccess: true,
                items: listResult.items,
                totalCount: listResult.totalCount,
                data: listResult
              } as ListQueryResponse;
            }
            
            // Check if it's directly a SerializableListQueryResult
            if ('compressedItemsJson' in result && result.compressedItemsJson) {
              console.log(`[SekibanDaprExecutor] Deserializing compressed query result directly`);
              const deserializeResult = await deserializeListQueryResult(
                result as any,
                this.domainTypes
              );
              
              if (deserializeResult.isErr()) {
                console.error(`[SekibanDaprExecutor] Failed to deserialize:`, deserializeResult.error);
                return {
                  isSuccess: false,
                  error: deserializeResult.error.message
                };
              }
              
              const listResult = deserializeResult.value;
              return {
                isSuccess: true,
                items: listResult.items,
                totalCount: listResult.totalCount,
                data: listResult
              } as ListQueryResponse;
            }
            
            // If it has the expected properties, return as-is
            if ('isSuccess' in result && 'items' in result) {
              return result as ListQueryResponse;
            }
            
            // Fallback for uncompressed results
            return {
              isSuccess: true,
              items: (result as any).items || [],
              totalCount: (result as any).totalCount || 0,
              data: result
            } as ListQueryResponse;
          }
          
          return {
            isSuccess: false,
            error: 'Invalid response format'
          };
        } catch (error) {
          console.error(`[SekibanDaprExecutor] Network error calling actor:`, error);
          return {
            isSuccess: false,
            error: error instanceof Error ? error.message : 'Unknown error'
          };
        }
      },
      
      isSortableUniqueIdReceived: async (sortableUniqueId: string): Promise<boolean> => {
        // Not implemented for now
        return false;
      },
      
      buildStateAsync: async (): Promise<ActorMultiProjectionState> => {
        // Not implemented for now
        throw new Error('Not implemented');
      },
      
      rebuildStateAsync: async (): Promise<void> => {
        // Not implemented for now
        throw new Error('Not implemented');
      },
      
      handlePublishedEvent: async (envelope: DaprEventEnvelope): Promise<void> => {
        // Not implemented for now
        throw new Error('Not implemented');
      }
    };
    
    return proxy;
  }
  
  /**
   * Execute query through Dapr actor
   */
  async queryAsync<T>(query: any): Promise<Result<T, SekibanError>> {
    try {
      // Check if this is a multi-projection query
      if (this.isMultiProjectionQuery(query)) {
        const projectorName = this.getMultiProjectorName(query);
        if (!projectorName) {
          return err(new QueryExecutionError('query', 'Unable to determine multi-projector name for query'));
        }
        
        console.log(`[SekibanDaprExecutor] Executing multi-projection query with projector: ${projectorName}`);
        
        // Create multi-projector actor proxy
        const multiProjectorActor = this.createMultiProjectorActorProxy(projectorName);
        
        // Convert query to serializable format
        const serializableQuery: SerializableQuery = {
          queryType: query.constructor.name,
          payload: query,
          partitionKeys: query.getPartitionKeys ? query.getPartitionKeys() : undefined
        };
        
        // Check if it's a list query
        // A query is a list query if:
        // 1. It has limit or offset properties
        // 2. It implements IMultiProjectionListQuery (check by method existence)
        // 3. The query type name contains "List"
        const isListQuery = query.limit !== undefined || 
                           query.offset !== undefined ||
                           query.constructor.name.includes('List') ||
                           (query.handleFilter && typeof query.handleFilter === 'function') ||
                           (query.constructor.handleFilter && typeof query.constructor.handleFilter === 'function');
        
        let response: QueryResponse | ListQueryResponse;
        if (isListQuery) {
          const listQuery: SerializableListQuery = {
            ...serializableQuery,
            limit: query.limit,
            skip: query.offset
          };
          response = await this.executeWithRetry(
            () => multiProjectorActor.queryListAsync(listQuery),
            'Multi-projection list query execution'
          );
        } else {
          response = await this.executeWithRetry(
            () => multiProjectorActor.queryAsync(serializableQuery),
            'Multi-projection query execution'
          );
        }
        
        if (!response.isSuccess) {
          return err(new QueryExecutionError('query', response.error || 'Query execution failed'));
        }
        
        // Return the data from the response
        return ok((isListQuery ? (response as ListQueryResponse).items : response.data) as T);
      }
      
      // Original single-aggregate query logic
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
        constructor(message: string) {
          super(message);
        }
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
          constructor(message: string) {
            super(message);
          }
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
        constructor(message: string) {
          super(message);
        }
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
    return projectorInfos.map((info: any) => info.projector as unknown as IAggregateProjector<any>);
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