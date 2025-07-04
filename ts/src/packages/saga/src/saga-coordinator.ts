import { Result, ok, err } from '../../core/src/result';
import {
  ChoreographySaga,
  SagaReaction,
  ReactionTrigger,
  ReactionAction,
  ReactionContext,
  ReactionResult,
  TimeoutTracker,
  PolicyTracker,
  ChoreographyState
} from './choreography-types';
import {
  ICommand,
  IEventPayload,
  EventDocument,
  SekibanError
} from '../../core/src';
import { SagaError } from './errors';

/**
 * Event store interface for choreography
 */
export interface IEventStore {
  append(event: { eventType: string; payload: IEventPayload }): Promise<Result<void, SekibanError>>;
  getEvents(filter: {
    eventTypes?: string[];
    correlationKey?: string;
    correlationValue?: string;
    since?: Date;
  }): Promise<Result<EventDocument<IEventPayload>[], SekibanError>>;
}

/**
 * Command executor interface
 */
export interface ICommandExecutor {
  execute(command: ICommand & { type: string }): Promise<Result<EventDocument<IEventPayload>[], SekibanError>>;
}

/**
 * Coordinator configuration
 */
export interface CoordinatorConfig {
  eventStore: IEventStore;
  commandExecutor: ICommandExecutor;
}

/**
 * Coordinates choreography-based sagas
 */
export class SagaCoordinator {
  private choreographies = new Map<string, ChoreographySaga>();
  private state: ChoreographyState = {
    activeTimeouts: [],
    policyTrackers: new Map(),
    reactionHistory: []
  };
  private eventStore: IEventStore;
  private commandExecutor: ICommandExecutor;
  private timeoutTimers = new Map<string, NodeJS.Timeout>();

  constructor(config: CoordinatorConfig) {
    this.eventStore = config.eventStore;
    this.commandExecutor = config.commandExecutor;
  }

  /**
   * Register a choreography saga
   */
  register(choreography: ChoreographySaga): void {
    this.choreographies.set(choreography.name, choreography);
  }

  /**
   * Get a choreography by name
   */
  getChoreography(name: string): ChoreographySaga | undefined {
    return this.choreographies.get(name);
  }

  /**
   * Handle an event and trigger appropriate reactions
   */
  async handleEvent(event: EventDocument<IEventPayload>): Promise<Result<void, SagaError>> {
    const reactions: Array<{ choreography: ChoreographySaga; reaction: SagaReaction }> = [];

    // Find matching reactions
    for (const choreography of this.choreographies.values()) {
      for (const reaction of choreography.reactions) {
        if (await this.shouldTriggerReaction(event, reaction, choreography.name)) {
          reactions.push({ choreography, reaction });
        }
      }
    }

    // Execute reactions
    for (const { choreography, reaction } of reactions) {
      const result = await this.executeReaction(event, reaction, choreography.name);
      if (result.isErr()) {
        return err(result.error);
      }

      // Handle chained reactions
      if (reaction.chain) {
        await this.handleChainedReactions(event, reaction.chain, choreography.name);
      }
    }

    return ok(undefined);
  }

  /**
   * Check if a reaction should be triggered
   */
  private async shouldTriggerReaction(
    event: EventDocument<IEventPayload>,
    reaction: SagaReaction,
    choreographyName: string
  ): Promise<boolean> {
    const trigger = reaction.trigger;

    // Check event type
    if (trigger.eventType !== event.eventType) {
      return false;
    }

    // Check condition
    if (trigger.condition && !trigger.condition(event)) {
      return false;
    }

    // Check correlation
    if (trigger.correlation) {
      const correlationMet = await this.checkCorrelation(event, trigger.correlation);
      if (!correlationMet) {
        return false;
      }
    }

    // Check policy
    if (trigger.policy) {
      const policyMet = await this.checkPolicy(event, reaction, choreographyName);
      if (!policyMet) {
        return false;
      }
    }

    return true;
  }

  /**
   * Check correlation requirements
   */
  private async checkCorrelation(
    event: EventDocument<IEventPayload>,
    correlation: NonNullable<ReactionTrigger['correlation']>
  ): Promise<boolean> {
    const correlationValue = (event.payload as any)[correlation.key];
    if (!correlationValue) {
      return false;
    }

    // Get related events
    const since = correlation.within
      ? new Date(Date.now() - correlation.within)
      : undefined;

    const result = await this.eventStore.getEvents({
      eventTypes: correlation.requires,
      correlationKey: correlation.key,
      correlationValue,
      since
    });

    if (result.isErr()) {
      return false;
    }

    // Check if all required events exist
    const foundEventTypes = new Set(result.value.map(e => e.eventType));
    return correlation.requires.every(type => foundEventTypes.has(type));
  }

  /**
   * Check policy constraints
   */
  private async checkPolicy(
    event: EventDocument<IEventPayload>,
    reaction: SagaReaction,
    choreographyName: string
  ): Promise<boolean> {
    const policy = reaction.trigger.policy!;
    const trackerId = `${choreographyName}:${reaction.name}:${event.eventType}`;
    
    let tracker = this.state.policyTrackers.get(trackerId);
    if (!tracker) {
      tracker = {
        sagaName: choreographyName,
        reactionName: reaction.name,
        eventType: event.eventType,
        occurrences: []
      };
      this.state.policyTrackers.set(trackerId, tracker);
    }

    // Clean old occurrences
    const cutoff = Date.now() - policy.window;
    tracker.occurrences = tracker.occurrences.filter(
      o => o.timestamp.getTime() > cutoff
    );

    // Check if we're within limit
    if (tracker.occurrences.length >= policy.maxOccurrences) {
      return false;
    }

    // Add this occurrence
    tracker.occurrences.push({
      eventId: event.id,
      timestamp: event.timestamp
    });

    return true;
  }

  /**
   * Execute a reaction
   */
  private async executeReaction(
    event: EventDocument<IEventPayload>,
    reaction: SagaReaction,
    choreographyName: string
  ): Promise<Result<void, SagaError>> {
    const startTime = Date.now();
    const context: ReactionContext = {
      sagaName: choreographyName,
      reactionName: reaction.name,
      triggerEvent: event,
      executionTime: new Date()
    };

    try {
      // Setup timeout if needed
      if (reaction.trigger.timeout) {
        this.setupTimeout(event, reaction, choreographyName);
      }

      // Execute action
      const actionResult = await this.executeAction(reaction.action, event);
      
      if (actionResult.isErr()) {
        throw actionResult.error;
      }

      // Record success
      const result: ReactionResult = {
        context,
        success: true,
        output: actionResult.value,
        duration: Date.now() - startTime
      };
      this.state.reactionHistory.push(result);

      return ok(undefined);
    } catch (error) {
      // Record failure
      const result: ReactionResult = {
        context,
        success: false,
        error: error instanceof Error ? error : new Error(String(error)),
        duration: Date.now() - startTime
      };
      this.state.reactionHistory.push(result);

      return err(new SagaError(
        `Reaction ${reaction.name} failed`,
        undefined,
        choreographyName,
        reaction.name,
        error instanceof Error ? error : undefined
      ));
    }
  }

  /**
   * Execute a reaction action
   */
  private async executeAction(
    action: ReactionAction,
    event: EventDocument<IEventPayload>
  ): Promise<Result<any, Error>> {
    switch (action.type) {
      case 'command':
        if (!action.command) {
          return err(new Error('Command action missing command function'));
        }
        const command = action.command(event);
        const commandResult = await this.commandExecutor.execute(command);
        return commandResult.mapErr(e => e as Error);

      case 'event':
        if (!action.event) {
          return err(new Error('Event action missing event function'));
        }
        const newEvent = action.event(event);
        const appendResult = await this.eventStore.append(newEvent);
        return appendResult.mapErr(e => e as Error);

      case 'query':
        // TODO: Implement query execution
        return ok(undefined);

      default:
        return err(new Error(`Unknown action type: ${action.type}`));
    }
  }

  /**
   * Setup timeout for a reaction
   */
  private setupTimeout(
    event: EventDocument<IEventPayload>,
    reaction: SagaReaction,
    choreographyName: string
  ): void {
    const timeout = reaction.trigger.timeout!;
    const timeoutId = `${choreographyName}:${reaction.name}:${event.id}`;

    // Create timeout tracker
    const tracker: TimeoutTracker = {
      id: timeoutId,
      sagaName: choreographyName,
      reactionName: reaction.name,
      triggerEvent: event,
      timeoutAt: new Date(Date.now() + timeout.duration),
      action: timeout.action,
      unless: timeout.unless
    };

    this.state.activeTimeouts.push(tracker);

    // Setup timer
    const timer = setTimeout(() => {
      this.handleTimeout(tracker);
    }, timeout.duration);

    this.timeoutTimers.set(timeoutId, timer);
  }

  /**
   * Handle a timeout
   */
  private async handleTimeout(tracker: TimeoutTracker): Promise<void> {
    // Check if timeout should be cancelled
    if (tracker.unless) {
      const correlationKey = this.findCorrelationKey(tracker.triggerEvent);
      const correlationValue = correlationKey
        ? (tracker.triggerEvent.payload as any)[correlationKey]
        : undefined;

      if (correlationValue) {
        const result = await this.eventStore.getEvents({
          eventTypes: tracker.unless,
          correlationKey,
          correlationValue,
          since: tracker.triggerEvent.timestamp
        });

        if (result.isOk() && result.value.length > 0) {
          // Cancel timeout
          this.cancelTimeout(tracker.id);
          return;
        }
      }
    }

    // Execute timeout action
    await this.executeAction(tracker.action, tracker.triggerEvent);

    // Clean up
    this.cancelTimeout(tracker.id);
  }

  /**
   * Cancel a timeout
   */
  private cancelTimeout(timeoutId: string): void {
    const timer = this.timeoutTimers.get(timeoutId);
    if (timer) {
      clearTimeout(timer);
      this.timeoutTimers.delete(timeoutId);
    }

    this.state.activeTimeouts = this.state.activeTimeouts.filter(
      t => t.id !== timeoutId
    );
  }

  /**
   * Process all active timeouts
   */
  async processTimeouts(): Promise<void> {
    const now = Date.now();
    const expiredTimeouts = this.state.activeTimeouts.filter(
      t => t.timeoutAt.getTime() <= now
    );

    for (const timeout of expiredTimeouts) {
      await this.handleTimeout(timeout);
    }
  }

  /**
   * Handle chained reactions
   */
  private async handleChainedReactions(
    originalEvent: EventDocument<IEventPayload>,
    chain: SagaReaction[],
    choreographyName: string
  ): Promise<void> {
    // Chain reactions are registered temporarily
    const tempChoreography: ChoreographySaga = {
      name: `${choreographyName}_chain_${Date.now()}`,
      version: 1,
      reactions: chain
    };

    this.register(tempChoreography);

    // Clean up after a delay
    setTimeout(() => {
      this.choreographies.delete(tempChoreography.name);
    }, 60000); // 1 minute
  }

  /**
   * Find correlation key in event payload
   */
  private findCorrelationKey(event: EventDocument<IEventPayload>): string | undefined {
    const payload = event.payload as any;
    
    // Common correlation keys
    const commonKeys = ['orderId', 'customerId', 'transactionId', 'correlationId'];
    
    for (const key of commonKeys) {
      if (payload[key] !== undefined) {
        return key;
      }
    }

    return undefined;
  }

  /**
   * Get choreography state
   */
  getState(): ChoreographyState {
    return this.state;
  }

  /**
   * Clear state (for testing)
   */
  clearState(): void {
    // Cancel all timeouts
    for (const timer of this.timeoutTimers.values()) {
      clearTimeout(timer);
    }
    this.timeoutTimers.clear();

    // Reset state
    this.state = {
      activeTimeouts: [],
      policyTrackers: new Map(),
      reactionHistory: []
    };
  }
}