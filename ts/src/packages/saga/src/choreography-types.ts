import { ICommand, IEventPayload, EventDocument } from '../../core/src';

/**
 * Reaction trigger conditions
 */
export interface ReactionTrigger {
  eventType: string;
  condition?: (event: EventDocument<IEventPayload>) => boolean;
  correlation?: {
    key: string;
    requires: string[]; // Required event types
    within?: number; // Time window in ms
  };
  timeout?: {
    duration: number;
    action: ReactionAction;
    unless?: string[]; // Cancel timeout if these events occur
  };
  policy?: {
    maxOccurrences: number;
    window: number; // Time window in ms
  };
}

/**
 * Reaction action types
 */
export interface ReactionAction {
  type: 'command' | 'event' | 'query';
  command?: (trigger: EventDocument<IEventPayload>) => ICommand & { type: string };
  event?: (trigger: EventDocument<IEventPayload>) => { eventType: string; payload: IEventPayload };
  query?: (trigger: EventDocument<IEventPayload>) => { type: string; parameters: any };
}

/**
 * Saga reaction definition
 */
export interface SagaReaction {
  name: string;
  trigger: ReactionTrigger;
  action: ReactionAction;
  chain?: SagaReaction[]; // Chained reactions
  compensate?: ReactionAction; // Compensation action
  metadata?: Record<string, any>;
}

/**
 * Choreography-based saga definition
 */
export interface ChoreographySaga {
  name: string;
  version: number;
  reactions: SagaReaction[];
  metadata?: {
    description?: string;
    tags?: string[];
    [key: string]: any;
  };
}

/**
 * Reaction execution context
 */
export interface ReactionContext {
  sagaName: string;
  reactionName: string;
  triggerEvent: EventDocument<IEventPayload>;
  correlatedEvents?: EventDocument<IEventPayload>[];
  executionTime: Date;
}

/**
 * Reaction execution result
 */
export interface ReactionResult {
  context: ReactionContext;
  success: boolean;
  output?: any;
  error?: Error;
  duration: number;
}

/**
 * Timeout tracking
 */
export interface TimeoutTracker {
  id: string;
  sagaName: string;
  reactionName: string;
  triggerEvent: EventDocument<IEventPayload>;
  timeoutAt: Date;
  action: ReactionAction;
  unless?: string[];
}

/**
 * Policy tracking
 */
export interface PolicyTracker {
  sagaName: string;
  reactionName: string;
  eventType: string;
  occurrences: Array<{
    eventId: string;
    timestamp: Date;
  }>;
}

/**
 * Choreography state for tracking
 */
export interface ChoreographyState {
  activeTimeouts: TimeoutTracker[];
  policyTrackers: Map<string, PolicyTracker>;
  reactionHistory: ReactionResult[];
}

/**
 * Reaction condition helper
 */
export class ReactionCondition {
  static hasAmount(minAmount: number): (event: EventDocument<IEventPayload>) => boolean {
    return (event) => {
      const payload = event.payload as any;
      return payload.amount !== undefined && payload.amount >= minAmount;
    };
  }

  static hasField(fieldName: string, value?: any): (event: EventDocument<IEventPayload>) => boolean {
    return (event) => {
      const payload = event.payload as any;
      if (value === undefined) {
        return payload[fieldName] !== undefined;
      }
      return payload[fieldName] === value;
    };
  }

  static and(...conditions: Array<(event: EventDocument<IEventPayload>) => boolean>): (event: EventDocument<IEventPayload>) => boolean {
    return (event) => conditions.every(condition => condition(event));
  }

  static or(...conditions: Array<(event: EventDocument<IEventPayload>) => boolean>): (event: EventDocument<IEventPayload>) => boolean {
    return (event) => conditions.some(condition => condition(event));
  }

  static not(condition: (event: EventDocument<IEventPayload>) => boolean): (event: EventDocument<IEventPayload>) => boolean {
    return (event) => !condition(event);
  }
}