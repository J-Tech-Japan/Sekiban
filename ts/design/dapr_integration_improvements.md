# Dapr Integration Improvements for Sekiban.ts

## Issues Encountered During Implementation

### 1. Actor Invocation Complexity

**Problem**: The current implementation uses `daprClient.invoker.invoke()` with manual URL construction for actor methods, which is error-prone and doesn't match Dapr SDK patterns.

**Current Code**:
```typescript
const response = await this.daprClient.invoker.invoke(
  this.configuration.actorIdPrefix || 'sekiban',
  `actors/${actorType}/${actorId}/method/executeCommandAsync`,
  HttpMethod.POST,
  commandAndMetadata
);
```

**Improvement**:
```typescript
// Use Dapr's actor proxy pattern
export class SekibanDaprExecutor {
  private actorProxyBuilder: DaprActorProxyBuilder;
  
  constructor(
    daprClient: DaprClient,
    domainTypes: SekibanDomainTypes,
    config: DaprSekibanConfiguration
  ) {
    this.actorProxyBuilder = new DaprActorProxyBuilder<IAggregateActor>(
      daprClient,
      config.actorType
    );
  }
  
  private createActorProxy(actorId: string): IAggregateActor {
    return this.actorProxyBuilder.build(actorId);
  }
  
  async executeCommandAsync(command: ICommand): Promise<Result> {
    const actorProxy = this.createActorProxy(actorId);
    return actorProxy.executeCommandAsync(commandAndMetadata);
  }
}
```

### 2. Missing Actor Interface Definition

**Problem**: No clear TypeScript interface for the AggregateActor, making it difficult to ensure client-server contract.

**Improvement**:
```typescript
// Define actor interface matching C# implementation
export interface IAggregateActor {
  executeCommandAsync(
    command: SerializableCommandAndMetadata
  ): Promise<SekibanCommandResponse>;
  
  queryAsync<T>(query: any): Promise<T>;
  
  loadAggregateAsync(
    partitionKeys: PartitionKeys
  ): Promise<Aggregate>;
  
  getStateAsync(): Promise<AggregateState>;
  
  subscribeToEvents(
    callback: EventCallback
  ): Promise<SubscriptionId>;
}

// Implement actor with proper Dapr decorators
@DaprActor()
export class AggregateActor implements IAggregateActor {
  constructor(
    private readonly stateManager: DaprActorStateManager,
    private readonly eventStore: IEventStore,
    private readonly projector: IAggregateProjector
  ) {}
  
  @DaprActorMethod()
  async executeCommandAsync(
    command: SerializableCommandAndMetadata
  ): Promise<SekibanCommandResponse> {
    // Implementation
  }
}
```

### 3. Configuration and Discovery Issues

**Problem**: Hard-coded app IDs and manual configuration make it difficult to work in different environments.

**Improvement**:
```typescript
export interface DaprSekibanConfiguration {
  // Required
  stateStoreName: string;
  pubSubName: string;
  eventTopicName: string;
  actorType: string;
  
  // Auto-discovery options
  discovery?: {
    useServiceDiscovery: boolean;
    serviceName?: string;
    namespace?: string;
  };
  
  // Environment-specific overrides
  environments?: {
    [key: string]: Partial<DaprSekibanConfiguration>;
  };
}

// Configuration factory with environment support
export function createDaprConfiguration(
  base: DaprSekibanConfiguration,
  environment?: string
): DaprSekibanConfiguration {
  const env = environment || process.env.NODE_ENV || 'development';
  const envConfig = base.environments?.[env] || {};
  
  return {
    ...base,
    ...envConfig,
    // Auto-detect Dapr sidecar
    daprPort: process.env.DAPR_HTTP_PORT || '3500',
    daprGrpcPort: process.env.DAPR_GRPC_PORT || '50001',
    appId: process.env.DAPR_APP_ID || 'sekiban-app'
  };
}
```

### 4. Better Error Messages

**Problem**: Generic errors like "DAPR_SIDECAR_COULD_NOT_BE_STARTED" don't help developers understand what went wrong.

**Improvement**:
```typescript
export class DaprConnectionError extends SekibanError {
  constructor(
    public readonly details: {
      attempted: string;
      daprPort: string;
      appId: string;
      lastError: Error;
    }
  ) {
    super(`Failed to connect to Dapr sidecar at port ${details.daprPort}.
    
Troubleshooting steps:
1. Ensure Dapr is installed: dapr --version
2. Check if Dapr is running: dapr list
3. Start your app with Dapr: dapr run --app-id ${details.appId} --app-port 3000 -- npm start
4. Verify Dapr components are configured in ./dapr-components/

Last error: ${details.lastError.message}`);
  }
}

// Enhanced connection logic
private async connectToDapr(): Promise<void> {
  const maxAttempts = 3;
  let lastError: Error;
  
  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    try {
      // Try health check
      await this.daprClient.health();
      
      // Verify actor subsystem
      const metadata = await this.daprClient.getMetadata();
      if (!metadata.actors?.includes(this.config.actorType)) {
        throw new Error(
          `Actor type '${this.config.actorType}' not registered. ` +
          `Available actors: ${metadata.actors?.join(', ') || 'none'}`
        );
      }
      
      return; // Success
    } catch (error) {
      lastError = error as Error;
      
      if (attempt < maxAttempts) {
        await this.delay(Math.pow(2, attempt) * 1000);
      }
    }
  }
  
  throw new DaprConnectionError({
    attempted: `${maxAttempts} attempts`,
    daprPort: this.config.daprPort,
    appId: this.config.appId,
    lastError
  });
}
```

### 5. Development Mode Support

**Problem**: Requiring Dapr for development slows down the feedback loop.

**Improvement**:
```typescript
export interface ExecutorFactory {
  create(config: ExecutorConfig): Promise<ISekibanExecutor>;
}

export class SmartExecutorFactory implements ExecutorFactory {
  async create(config: ExecutorConfig): Promise<ISekibanExecutor> {
    // Auto-detect environment
    if (config.mode === 'auto') {
      const isDaprAvailable = await this.checkDaprAvailable();
      config.mode = isDaprAvailable ? 'dapr' : 'in-memory';
      
      console.log(`Auto-detected mode: ${config.mode}`);
    }
    
    switch (config.mode) {
      case 'dapr':
        return this.createDaprExecutor(config);
        
      case 'in-memory':
        console.warn('Running in in-memory mode. State will not persist.');
        return this.createInMemoryExecutor(config);
        
      case 'hybrid':
        // Use in-memory for queries, Dapr for commands
        return this.createHybridExecutor(config);
        
      default:
        throw new Error(`Unknown executor mode: ${config.mode}`);
    }
  }
  
  private async checkDaprAvailable(): Promise<boolean> {
    try {
      const response = await fetch(
        `http://localhost:${process.env.DAPR_HTTP_PORT || 3500}/v1.0/healthz`
      );
      return response.ok;
    } catch {
      return false;
    }
  }
}
```

### 6. Testing Support

**Problem**: Testing with Dapr requires complex setup.

**Improvement**:
```typescript
// Test utilities
export class DaprTestHarness {
  private processes: ChildProcess[] = [];
  
  async start(config: {
    apps: Array<{
      appId: string;
      appPort: number;
      command: string;
    }>;
    components?: string;
  }): Promise<void> {
    // Start Dapr placement service
    await this.startPlacement();
    
    // Start apps with Dapr
    for (const app of config.apps) {
      const daprProcess = spawn('dapr', [
        'run',
        '--app-id', app.appId,
        '--app-port', app.appPort.toString(),
        '--components-path', config.components || './test-components',
        '--',
        ...app.command.split(' ')
      ]);
      
      this.processes.push(daprProcess);
      
      // Wait for app to be ready
      await this.waitForApp(app.appPort);
    }
  }
  
  async stop(): Promise<void> {
    for (const process of this.processes) {
      process.kill();
    }
    
    // Clean up Dapr state
    await this.cleanup();
  }
}

// Usage in tests
describe('Dapr integration tests', () => {
  const harness = new DaprTestHarness();
  
  beforeAll(async () => {
    await harness.start({
      apps: [{
        appId: 'test-api',
        appPort: 3000,
        command: 'npm run start:test'
      }]
    });
  });
  
  afterAll(async () => {
    await harness.stop();
  });
  
  test('should execute commands through Dapr actors', async () => {
    // Test implementation
  });
});
```

### 7. Observability

**Problem**: Difficult to debug what's happening inside Dapr actors.

**Improvement**:
```typescript
export interface DaprObservability {
  tracing?: {
    enabled: boolean;
    samplingRate?: number;
    endpoint?: string;
  };
  
  metrics?: {
    enabled: boolean;
    port?: number;
  };
  
  logging?: {
    level: 'debug' | 'info' | 'warn' | 'error';
    format: 'json' | 'text';
  };
}

// Actor with built-in observability
export class ObservableAggregateActor extends AggregateActor {
  @Trace('actor.command')
  @Metrics('actor_commands_total')
  async executeCommandAsync(
    command: SerializableCommandAndMetadata
  ): Promise<SekibanCommandResponse> {
    const span = this.tracer.startSpan('executeCommand', {
      attributes: {
        'command.type': command.command.commandType,
        'aggregate.id': command.partitionKeys.aggregateId,
        'aggregate.type': command.partitionKeys.group
      }
    });
    
    try {
      const result = await super.executeCommandAsync(command);
      
      span.setStatus({ code: SpanStatusCode.OK });
      this.metrics.increment('commands.success', {
        command_type: command.command.commandType
      });
      
      return result;
    } catch (error) {
      span.recordException(error as Error);
      span.setStatus({ 
        code: SpanStatusCode.ERROR,
        message: error.message 
      });
      
      this.metrics.increment('commands.error', {
        command_type: command.command.commandType,
        error_type: error.constructor.name
      });
      
      throw error;
    } finally {
      span.end();
    }
  }
}
```

## Implementation Recommendations

1. **Start with Actor Interface**: Define clear contracts before implementation
2. **Add Development Mode**: Allow developers to work without Dapr
3. **Improve Error Messages**: Help developers understand and fix issues
4. **Create Test Utilities**: Make it easy to test Dapr integration
5. **Add Observability**: Built-in tracing and metrics for production debugging
6. **Document Setup**: Clear guides for different environments
7. **Provide Examples**: Working examples for common scenarios

## Conclusion

The Dapr integration in Sekiban.ts has great potential but needs refinement to be developer-friendly. Focus on:
- Clear abstractions that hide Dapr complexity
- Excellent error messages that guide developers
- Flexible configuration for different environments
- Strong typing throughout the stack
- Easy testing and debugging tools

These improvements will make Sekiban.ts with Dapr a powerful and approachable solution for distributed event sourcing.