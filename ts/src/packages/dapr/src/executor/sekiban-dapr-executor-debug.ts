import type { Result } from 'neverthrow';
import { ok, err } from 'neverthrow';
import type { DaprClient } from '@dapr/dapr';
import { HttpMethod } from '@dapr/dapr';
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

/**
 * Debug version of SekibanDaprExecutor with extensive logging
 */
export class SekibanDaprExecutor implements ISekibanDaprExecutor {
  constructor(
    private readonly daprClient: DaprClient,
    private readonly domainTypes: SekibanDomainTypes,
    private configuration: DaprSekibanConfiguration
  ) {
    console.log('[EXECUTOR CONSTRUCTOR] Creating SekibanDaprExecutor with config:', this.configuration);
    this.validateConfiguration(configuration);
  }
  
  private validateConfiguration(config: DaprSekibanConfiguration): void {
    if (!config.actorType) {
      config.actorType = 'AggregateActor';
    }
    console.log('[EXECUTOR CONFIG] Final configuration:', config);
  }
  
  
  private createActorProxy(actorId: string): IDaprAggregateActorProxy {
    console.log(`[ACTOR PROXY] Creating proxy for actor ID: ${actorId}`);
    
    const actorType = this.configuration.actorType;
    const appId = this.configuration.actorIdPrefix || 'sekiban-api';
    const daprPort = (this.daprClient as any).options?.daprPort || '3500';
    const daprHost = (this.daprClient as any).options?.daprHost || '127.0.0.1';
    
    console.log('[ACTOR PROXY] Actor configuration:', { actorType, appId, daprHost, daprPort });
    
    // Helper to make direct HTTP calls to Dapr actors
    const callActorMethod = async (methodName: string, data: any): Promise<any> => {
      const url = `http://${daprHost}:${daprPort}/v1.0/actors/${actorType}/${actorId}/method/${methodName}`;
      console.log(`[ACTOR HTTP] Calling ${methodName} at ${url}`);
      console.log('[ACTOR HTTP] Request data:', JSON.stringify(data, null, 2));
      
      const startTime = Date.now();
      
      try {
        console.log('[ACTOR HTTP] Making fetch request...');
        const response = await fetch(url, {
          method: 'PUT',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify(data)
        });
        
        const duration = Date.now() - startTime;
        console.log(`[ACTOR HTTP] Response received in ${duration}ms - Status: ${response.status}`);
        
        const responseText = await response.text();
        console.log('[ACTOR HTTP] Response body:', responseText);
        
        if (!response.ok) {
          console.error(`[ACTOR HTTP] Actor method ${methodName} failed with status ${response.status}`);
          throw new Error(`Actor method ${methodName} failed: ${response.status} ${responseText}`);
        }
        
        try {
          return JSON.parse(responseText);
        } catch (parseError) {
          console.log('[ACTOR HTTP] Response is not JSON, returning as text');
          return responseText;
        }
      } catch (fetchError) {
        const duration = Date.now() - startTime;
        console.error(`[ACTOR HTTP] Fetch failed after ${duration}ms:`, fetchError);
        throw fetchError;
      }
    };
    
    return {
      executeCommandAsync: async <
        TCommand,
        TProjector extends IAggregateProjector<TPayloadUnion>,
        TPayloadUnion extends ITypedAggregatePayload,
        TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
      >(
        commandAndMetadata: SerializableCommandAndMetadata<TCommand, TProjector, TPayloadUnion, TAggregatePayload>
      ): Promise<SekibanCommandResponse> => {
        console.log('[ACTOR PROXY] Executing command async...');
        return callActorMethod('executeCommandAsync', commandAndMetadata);
      },
      
      queryAsync: async <T>(query: any): Promise<T> => {
        console.log('[ACTOR PROXY] Executing query async...');
        return callActorMethod('queryAsync', query);
      },
      
      loadAggregateAsync: async <TPayload extends ITypedAggregatePayload>(
        partitionKeys: PartitionKeys
      ): Promise<Aggregate<TPayload>> => {
        console.log('[ACTOR PROXY] Loading aggregate async...');
        return callActorMethod('loadAggregateAsync', partitionKeys);
      }
    };
  }
  
  private async executeWithRetry<T>(
    operation: () => Promise<T>,
    operationName: string
  ): Promise<T> {
    const maxRetries = this.configuration.retryAttempts || 3;
    let lastError: Error;
    
    console.log(`[RETRY] Starting ${operationName} with max ${maxRetries} attempts`);
    
    for (let attempt = 1; attempt <= maxRetries; attempt++) {
      console.log(`[RETRY] Attempt ${attempt}/${maxRetries} for ${operationName}`);
      try {
        const result = await operation();
        console.log(`[RETRY] ${operationName} succeeded on attempt ${attempt}`);
        return result;
      } catch (error) {
        lastError = error instanceof Error ? error : new Error('Unknown error');
        console.error(`[RETRY] Attempt ${attempt} failed:`, lastError.message);
        
        if (attempt === maxRetries) {
          console.error(`[RETRY] ${operationName} failed after ${maxRetries} attempts`);
          throw new Error(`${operationName} failed after ${maxRetries} attempts: ${lastError.message}`);
        }
        
        const delay = Math.pow(2, attempt) * 100;
        console.log(`[RETRY] Waiting ${delay}ms before retry...`);
        await new Promise(resolve => setTimeout(resolve, delay));
      }
    }
    
    throw lastError!;
  }
  
  /**
   * Execute command through Dapr AggregateActor
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
    console.log('[EXECUTOR] executeCommandAsync called');
    
    // Handle case where command is passed as a single object (from defineCommand)
    let command: ICommandWithHandler<TCommand, TProjector, TPayloadUnion, TAggregatePayload>;
    let data: TCommand;
    
    if (commandData === undefined) {
      console.log('[EXECUTOR] Command passed as single instance');
      command = commandOrInstance;
      data = (commandOrInstance as any).data || {} as TCommand;
    } else {
      console.log('[EXECUTOR] Command and data passed separately');
      command = commandOrInstance as ICommandWithHandler<TCommand, TProjector, TPayloadUnion, TAggregatePayload>;
      data = commandData!;
    }
    
    console.log('[EXECUTOR] Command type:', (command as any).commandType || command.constructor.name);
    console.log('[EXECUTOR] Command data:', JSON.stringify(data, null, 2));
    
    try {
      // Validate command first
      console.log('[EXECUTOR] Validating command...');
      const validationResult = command.validate(data);
      if (validationResult.isErr()) {
        console.error('[EXECUTOR] Command validation failed:', validationResult.error);
        return err(validationResult.error as SekibanError);
      }
      console.log('[EXECUTOR] Command validation passed');
      
      // Get partition keys for the command
      console.log('[EXECUTOR] Getting partition keys...');
      const partitionKeys = command.specifyPartitionKeys(data);
      console.log('[EXECUTOR] Partition keys:', partitionKeys);
      
      // Get the projector from the command
      console.log('[EXECUTOR] Getting projector...');
      const projector = command.getProjector();
      console.log('[EXECUTOR] Projector:', projector.constructor.name);
      
      // Create PartitionKeysAndProjector
      console.log('[EXECUTOR] Creating PartitionKeysAndProjector...');
      const partitionKeyAndProjector = new PartitionKeysAndProjector(partitionKeys, projector);
      
      // Generate actor ID using the projector grain key format
      const actorId = partitionKeyAndProjector.toProjectorGrainKey();
      console.log('[EXECUTOR] Generated actor ID:', actorId);
      
      const actorProxy = this.createActorProxy(actorId);
      
      // Prepare command with metadata
      const commandTypeName = command.commandType || (command as any).type || command.constructor.name;
      const projectorTypeName = projector.constructor.name;
      
      const commandWithMetadata = {
        commandId: crypto.randomUUID(),
        causationId: metadata?.causationId || crypto.randomUUID(),
        correlationId: metadata?.correlationId || crypto.randomUUID(),
        executedUser: metadata?.executedUser || (metadata?.custom as any)?.user || 'system',
        commandTypeName,
        projectorTypeName,
        aggregatePayloadTypeName: '',
        commandData: data,
        commandAssemblyVersion: '1.0.0'
      };
      
      console.log('[EXECUTOR] Command with metadata:', JSON.stringify(commandWithMetadata, null, 2));
      
      // Execute command through actor with retry
      console.log('[EXECUTOR] Executing command through actor...');
      const responseStr = await this.executeWithRetry(
        () => actorProxy.executeCommandAsync(commandWithMetadata as any),
        'Command execution'
      );
      
      console.log('[EXECUTOR] Raw response:', responseStr);
      
      // Parse the JSON response
      const response = typeof responseStr === 'string' ? JSON.parse(responseStr) : responseStr;
      console.log('[EXECUTOR] Parsed response:', response);
      
      // Check if there's an error in the response
      if (response.error) {
        console.error('[EXECUTOR] Command execution returned error:', response.error);
        class CommandExecutionError extends SekibanError {
          readonly code = 'COMMAND_EXECUTION_ERROR';
        }
        return err(new CommandExecutionError(JSON.stringify(response.error)));
      }
      
      // Convert to SekibanCommandResponse format
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
      
      console.log('[EXECUTOR] Command execution successful:', commandResponse);
      return ok(commandResponse);
      
    } catch (error) {
      console.error('[EXECUTOR] Command execution failed with exception:', error);
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
    console.log('[EXECUTOR] queryAsync called with:', query);
    
    try {
      const actorId = `${this.configuration.actorIdPrefix || 'sekiban'}.query.${query.queryType}.${query.userId || 'default'}`;
      console.log('[EXECUTOR] Query actor ID:', actorId);
      
      const actorProxy = this.createActorProxy(actorId);
      
      console.log('[EXECUTOR] Executing query through actor...');
      const response = await this.executeWithRetry(
        () => actorProxy.queryAsync<T>(query),
        'Query execution'
      );
      
      console.log('[EXECUTOR] Query response:', response);
      return ok(response);
      
    } catch (error) {
      console.error('[EXECUTOR] Query execution failed:', error);
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
    console.log('[EXECUTOR] loadAggregateAsync called');
    console.log('[EXECUTOR] Projector:', projector.constructor.name);
    console.log('[EXECUTOR] Partition keys:', partitionKeys);
    
    try {
      // Verify the projector is registered
      const projectorConstructor = this.domainTypes.projectorTypes.getProjectorByAggregateType(projector.aggregateTypeName);
      if (!projectorConstructor) {
        console.error('[EXECUTOR] Projector not found for aggregate type:', projector.aggregateTypeName);
        class ProjectorNotFoundError extends SekibanError {
          readonly code = 'PROJECTOR_NOT_FOUND';
        }
        return err(new ProjectorNotFoundError(`No projector registered for aggregate type: ${projector.aggregateTypeName}`));
      }
      
      const partitionKeyAndProjector = new PartitionKeysAndProjector(partitionKeys, projector);
      const actorId = partitionKeyAndProjector.toProjectorGrainKey();
      console.log('[EXECUTOR] Load aggregate actor ID:', actorId);
      
      const actorProxy = this.createActorProxy(actorId);
      
      console.log('[EXECUTOR] Loading aggregate through actor...');
      const response = await this.executeWithRetry(
        () => actorProxy.loadAggregateAsync<TPayload>(partitionKeys),
        'Aggregate loading'
      );
      
      console.log('[EXECUTOR] Aggregate loaded:', response);
      return ok(response);
      
    } catch (error) {
      console.error('[EXECUTOR] Aggregate loading failed:', error);
      class DaprActorError extends SekibanError {
        readonly code = 'DAPR_ACTOR_ERROR';
      }
      return err(new DaprActorError(error instanceof Error ? error.message : 'Unknown actor error'));
    }
  }
  
  // Other methods remain the same...
  getDaprClient(): DaprClient {
    return this.daprClient;
  }
  
  getConfiguration(): DaprSekibanConfiguration {
    return { ...this.configuration };
  }
  
  getRegisteredProjectors(): IAggregateProjector<any>[] {
    const projectorInfos = this.domainTypes.projectorTypes.getProjectorTypes();
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