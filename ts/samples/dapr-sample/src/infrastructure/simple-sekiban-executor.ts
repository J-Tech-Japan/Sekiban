import { Result, ok, err } from 'neverthrow';
import type { MetricsStore } from '../observability/metrics-store';

// Simplified interfaces for the TDD cycle
export interface ICommand {
  type: string;
  aggregateId: string;
}

export interface IQuery<T> {
  type: string;
}

export interface IEvent {
  type: string;
  aggregateId: string;
  data: any;
  version: number;
  occurredAt: string;
}

export interface SekibanExecutor {
  executeCommand(command: ICommand): Promise<Result<{ aggregateId: string }, Error>>;
  executeQuery<T>(query: IQuery<T>): Promise<Result<T, Error>>;
  seedEvents?(events: IEvent[]): void;
}

export interface SekibanConfig {
  storage: {
    type: 'postgres';
    postgres: {
      connectionString: string;
      maxConnections?: number;
      idleTimeoutMillis?: number;
      connectionTimeoutMillis?: number;
    };
  };
  dapr?: {
    httpPort: number;
    grpcPort: number;
    stateStoreName: string;
    pubSubName: string;
    appId: string;
  };
  server?: {
    port: number;
    host: string;
  };
  environment: string;
}

// Simple in-memory implementation for TDD
export class SimpleSekibanExecutor implements SekibanExecutor {
  private events: IEvent[] = [];
  private projections: Map<string, any> = new Map();
  private daprClient: any;
  private metricsStore: MetricsStore | undefined;
  
  constructor(private config: SekibanConfig, dependencies: any = {}) {
    this.daprClient = dependencies.daprClient;
    this.metricsStore = dependencies.metricsStore;
  }
  
  async executeCommand(command: ICommand): Promise<Result<{ aggregateId: string }, Error>> {
    try {
      if (command.type === 'CreateUser') {
        return await this.handleCreateUser(command);
      }
      
      return err(new Error(`Unknown command type: ${command.type}`));
    } catch (error) {
      return err(error instanceof Error ? error : new Error('Unknown error'));
    }
  }
  
  async executeQuery<T>(query: IQuery<T>): Promise<Result<T, Error>> {
    try {
      if (query.type === 'GetUser') {
        return await this.handleGetUser(query) as Result<T, Error>;
      }
      
      return err(new Error(`Unknown query type: ${query.type}`));
    } catch (error) {
      return err(error instanceof Error ? error : new Error('Unknown error'));
    }
  }
  
  private async handleCreateUser(command: ICommand): Promise<Result<{ aggregateId: string }, Error>> {
    const cmd = command as any;
    
    // Check if user with this email already exists
    const existingUser = Array.from(this.projections.values())
      .find((user: any) => user.email === cmd.email);
    
    if (existingUser) {
      return err(new Error('User with this email already exists'));
    }
    
    // Create and store event
    const event: IEvent = {
      type: 'UserRegistered',
      aggregateId: command.aggregateId,
      data: {
        userId: command.aggregateId,
        name: cmd.name,
        email: cmd.email,
        registeredAt: new Date().toISOString()
      },
      version: 1,
      occurredAt: new Date().toISOString()
    };
    
    this.events.push(event);
    
    // Update projection
    this.projections.set(command.aggregateId, {
      id: command.aggregateId,
      name: cmd.name,
      email: cmd.email,
      createdAt: event.data.registeredAt
    });
    
    // Publish event to Dapr pub/sub (fire-and-forget, don't fail user creation)
    this.publishEventAsync(event);
    
    // Record metrics
    if (this.metricsStore) {
      this.metricsStore.incrementCounter('user_registered_total', {
        email_domain: cmd.email.split('@')[1] || 'unknown'
      });
    }
    
    return ok({ aggregateId: command.aggregateId });
  }
  
  private async publishEventAsync(event: IEvent): Promise<void> {
    if (!this.daprClient?.pubsub?.publish) {
      console.warn('Dapr client not available, skipping event publishing');
      return;
    }
    
    try {
      await this.daprClient.pubsub.publish('users', 'UserRegistered', event.data);
      console.log(`Published event ${event.type} for aggregate ${event.aggregateId}`);
    } catch (error) {
      console.error(`Failed to publish event ${event.type}:`, error);
      // Don't throw - publishing failures shouldn't affect command execution
    }
  }
  
  private async handleGetUser(query: IQuery<any>): Promise<Result<any, Error>> {
    const q = query as any;
    
    // Check if this is a time-travel query
    if (q.asOf) {
      return this.handleTimeTravel(q.userId, q.asOf);
    }
    
    const user = this.projections.get(q.userId);
    
    if (!user) {
      return err(new Error('User not found'));
    }
    
    return ok(user);
  }
  
  private handleTimeTravel(userId: string, asOf: string): Result<any, Error> {
    const asOfDate = new Date(asOf);
    
    // Filter events for this aggregate up to the specified time
    const relevantEvents = this.events
      .filter(event => 
        event.aggregateId === userId && 
        new Date(event.occurredAt) <= asOfDate
      )
      .sort((a, b) => new Date(a.occurredAt).getTime() - new Date(b.occurredAt).getTime());
    
    if (relevantEvents.length === 0) {
      return err(new Error('User not found'));
    }
    
    // Replay events to reconstruct state at that point in time
    let state: any = null;
    
    for (const event of relevantEvents) {
      switch (event.type) {
        case 'UserRegistered':
          state = {
            id: event.aggregateId,
            name: event.data.name,
            email: event.data.email,
            createdAt: event.data.registeredAt
          };
          break;
        case 'UserEmailUpdated':
          if (state) {
            state.email = event.data.newEmail;
          }
          break;
        case 'UserNameUpdated':
          if (state) {
            state.name = event.data.newName;
          }
          break;
      }
    }
    
    if (!state) {
      return err(new Error('User not found'));
    }
    
    // Add time-travel metadata
    state._replayedAt = new Date().toISOString();
    state._eventsReplayed = relevantEvents.length;
    
    return ok(state);
  }
  
  // Test helper method for seeding events
  seedEvents(events: IEvent[]): void {
    // Add events to the store
    this.events.push(...events);
    
    // Update projections based on seeded events
    this.rebuildProjections();
  }
  
  private rebuildProjections(): void {
    this.projections.clear();
    
    // Group events by aggregate ID
    const eventsByAggregate = new Map<string, IEvent[]>();
    
    for (const event of this.events) {
      if (!eventsByAggregate.has(event.aggregateId)) {
        eventsByAggregate.set(event.aggregateId, []);
      }
      eventsByAggregate.get(event.aggregateId)!.push(event);
    }
    
    // Rebuild each aggregate's projection
    for (const [aggregateId, events] of eventsByAggregate) {
      events.sort((a, b) => new Date(a.occurredAt).getTime() - new Date(b.occurredAt).getTime());
      
      let state: any = null;
      
      for (const event of events) {
        switch (event.type) {
          case 'UserRegistered':
            state = {
              id: event.aggregateId,
              name: event.data.name,
              email: event.data.email,
              createdAt: event.data.registeredAt
            };
            break;
          case 'UserEmailUpdated':
            if (state) {
              state.email = event.data.newEmail;
            }
            break;
          case 'UserNameUpdated':
            if (state) {
              state.name = event.data.newName;
            }
            break;
        }
      }
      
      if (state) {
        this.projections.set(aggregateId, state);
      }
    }
  }
}