import { 
  ICommand, 
  IEventPayload, 
  IAggregatePayload,
  ICommandHandler,
  IProjector,
  EventDocument,
  PartitionKeys,
  SortableUniqueId,
  Result,
  SekibanError
} from '../../../core/src';
import { EventBuilder } from '../builders/event-builder';
import { expect } from 'vitest';

/**
 * Test context for running scenarios
 */
export interface TestContext<TCommand extends ICommand = ICommand> {
  commandHandler: ICommandHandler<TCommand>;
  projector: IProjector<any>;
  currentTime?: Date;
  snapshotVersion?: number;
}

/**
 * Multi-command test context
 */
export interface SequenceTestContext {
  commandHandlers: Record<string, ICommandHandler<any>>;
  projector: IProjector<any>;
  currentTime?: Date;
}

/**
 * DSL for writing event sourcing test scenarios
 */
export class TestScenario {
  private description: string = '';
  private givenEventsData: EventDocument<IEventPayload>[] = [];
  private givenSnapshotData?: { payload: IAggregatePayload; version: number };
  private whenCommandData?: ICommand & { type: string };
  private whenCommandsData?: Array<ICommand & { type: string }>;
  private partitionKeys?: PartitionKeys;
  private currentTime?: Date;
  private expectations: Array<() => Promise<void>> = [];
  private producedEvents: EventDocument<IEventPayload>[] = [];
  private resultingAggregate?: any;
  private commandError?: SekibanError;

  /**
   * Set the current time for the scenario
   */
  atTime(time: Date): TestScenario {
    this.currentTime = time;
    return this;
  }

  /**
   * Describe the initial state
   */
  given(description: string): TestScenario {
    this.description = description;
    return this;
  }

  /**
   * Set up initial events
   */
  givenEvents(
    events: Array<Partial<EventDocument<IEventPayload>> & { eventType: string; payload: IEventPayload }>,
    partitionKeys?: PartitionKeys
  ): TestScenario {
    this.partitionKeys = partitionKeys || PartitionKeys.create('test-aggregate', 'test');
    
    this.givenEventsData = events.map((event, index) => {
      const builder = new EventBuilder(event.eventType)
        .withPayload(event.payload)
        .withPartitionKeys(this.partitionKeys!)
        .withVersion(event.version || index + 1);
      
      if (event.timestamp) {
        builder.withTimestamp(event.timestamp);
      }
      
      return builder.build();
    });
    
    return this;
  }

  /**
   * Set up initial snapshot
   */
  givenSnapshot<TPayload extends IAggregatePayload>(
    payload: TPayload,
    version: number
  ): TestScenario {
    this.givenSnapshotData = { payload, version };
    return this;
  }

  /**
   * Execute a command
   */
  when<TCommand extends ICommand>(command: TCommand & { type: string }): TestScenario {
    this.whenCommandData = command;
    return this;
  }

  /**
   * Execute a sequence of commands
   */
  whenSequence(commands: Array<ICommand & { type: string }>): TestScenario {
    this.whenCommandsData = commands;
    return this;
  }

  /**
   * Start defining expectations
   */
  then(): TestScenario {
    return this;
  }

  /**
   * Expect specific events to be produced
   */
  expectEvents(
    expectedEvents: Array<{ eventType: string; payload: Partial<IEventPayload>; version?: number }> | 
    ((events: EventDocument<IEventPayload>[]) => void)
  ): TestScenario {
    this.expectations.push(async () => {
      if (typeof expectedEvents === 'function') {
        expectedEvents(this.producedEvents);
      } else {
        expect(this.producedEvents).toHaveLength(expectedEvents.length);
        
        expectedEvents.forEach((expected, index) => {
          const actual = this.producedEvents[index];
          expect(actual.eventType).toBe(expected.eventType);
          expect(actual.payload).toMatchObject(expected.payload);
          if (expected.version !== undefined) {
            expect(actual.version).toBe(expected.version);
          }
        });
      }
    });
    return this;
  }

  /**
   * Expect a specific event at index
   */
  expectEvent(index: number, assertion: (event: EventDocument<IEventPayload>) => void): TestScenario {
    this.expectations.push(async () => {
      expect(this.producedEvents).toHaveLength(Math.max(index + 1, this.producedEvents.length));
      assertion(this.producedEvents[index]);
    });
    return this;
  }

  /**
   * Expect specific number of events
   */
  expectEventCount(count: number): TestScenario {
    this.expectations.push(async () => {
      expect(this.producedEvents).toHaveLength(count);
    });
    return this;
  }

  /**
   * Expect no more events after assertions
   */
  expectNoMoreEvents(): TestScenario {
    return this;
  }

  /**
   * Expect aggregate state
   */
  expectAggregateState<TPayload extends IAggregatePayload>(
    expected: Partial<TPayload> | ((state: TPayload) => void)
  ): TestScenario {
    this.expectations.push(async () => {
      expect(this.resultingAggregate).toBeDefined();
      
      if (typeof expected === 'function') {
        expected(this.resultingAggregate);
      } else {
        expect(this.resultingAggregate).toMatchObject(expected);
      }
    });
    return this;
  }

  /**
   * Expect aggregate version
   */
  expectAggregateVersion(version: number): TestScenario {
    this.expectations.push(async () => {
      const lastEvent = this.producedEvents[this.producedEvents.length - 1];
      expect(lastEvent?.version || 0).toBe(version);
    });
    return this;
  }

  /**
   * Expect command to fail with error
   */
  expectError(code: string, message?: string): TestScenario {
    this.expectations.push(async () => {
      expect(this.commandError).toBeDefined();
      expect(this.commandError?.code).toBe(code);
      if (message) {
        expect(this.commandError?.message).toContain(message);
      }
    });
    return this;
  }

  /**
   * Run the scenario
   */
  async run<TCommand extends ICommand>(context: TestContext<TCommand>): Promise<void> {
    if (!this.whenCommandData) {
      throw new Error('No command specified. Use .when() to specify a command');
    }

    // Set up initial state
    const initialAggregate = this.givenSnapshotData
      ? context.projector.project(this.givenEventsData)
      : context.projector.project(this.givenEventsData);

    // Execute command
    const result = await context.commandHandler.handle(
      this.whenCommandData as TCommand,
      initialAggregate
    );

    if (result.isOk()) {
      const startVersion = context.snapshotVersion || this.givenEventsData.length;
      this.producedEvents = result.value.map((event, index) => {
        const builder = new EventBuilder(event.eventType)
          .withPayload(event.payload)
          .withVersion(startVersion + index + 1)
          .withPartitionKeys(this.partitionKeys!);
        
        // Use context time first, then current time, then event time
        if (context.currentTime) {
          builder.withTimestamp(context.currentTime);
        } else if (this.currentTime) {
          builder.withTimestamp(this.currentTime);
        } else if ('timestamp' in event && event.timestamp) {
          builder.withTimestamp(event.timestamp as Date);
        }
        
        return builder.build();
      });

      // Project final state
      const allEvents = [...this.givenEventsData, ...this.producedEvents];
      this.resultingAggregate = context.projector.project(allEvents);
    } else {
      this.commandError = result.error;
    }

    // Run expectations
    for (const expectation of this.expectations) {
      await expectation();
    }
  }

  /**
   * Run a sequence of commands
   */
  async runSequence(context: SequenceTestContext): Promise<void> {
    if (!this.whenCommandsData || this.whenCommandsData.length === 0) {
      throw new Error('No commands specified. Use .whenSequence() to specify commands');
    }

    let allEvents = [...this.givenEventsData];
    
    for (const command of this.whenCommandsData) {
      const handler = context.commandHandlers[command.type];
      if (!handler) {
        throw new Error(`No handler found for command type: ${command.type}`);
      }

      const currentAggregate = context.projector.project(allEvents);
      const result = await handler.handle(command, currentAggregate);

      if (result.isOk()) {
        const newEvents = result.value.map((event, index) => {
          const builder = new EventBuilder(event.eventType)
            .withPayload(event.payload)
            .withVersion(allEvents.length + index + 1)
            .withPartitionKeys(this.partitionKeys!);
          
          if (this.currentTime || context.currentTime) {
            builder.withTimestamp(this.currentTime || context.currentTime!);
          }
          
          return builder.build();
        });
        
        this.producedEvents.push(...newEvents);
        allEvents.push(...newEvents);
      } else {
        this.commandError = result.error;
        break;
      }
    }

    // Project final state
    this.resultingAggregate = context.projector.project(allEvents);

    // Run expectations
    for (const expectation of this.expectations) {
      await expectation();
    }
  }
}