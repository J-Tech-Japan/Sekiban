import { describe, it, expect, vi } from 'vitest';
import { TestScenario } from './scenario-dsl';
import { 
  ICommand, 
  IEventPayload, 
  IAggregatePayload,
  ICommandHandler,
  IProjector,
  PartitionKeys,
  EventDocument,
  ok,
  err,
  ValidationError
} from '@sekiban/core';

// Test domain types
interface CreateUser extends ICommand {
  name: string;
  email: string;
}

interface UpdateUser extends ICommand {
  userId: string;
  name?: string;
  email?: string;
}

interface UserCreated extends IEventPayload {
  userId: string;
  name: string;
  email: string;
}

interface UserUpdated extends IEventPayload {
  userId: string;
  name?: string;
  email?: string;
}

interface UserAggregate extends IAggregatePayload {
  userId: string;
  name: string;
  email: string;
  version: number;
}

// Mock projector
class UserProjector implements IProjector<UserAggregate> {
  project(events: EventDocument<IEventPayload>[]): UserAggregate | undefined {
    if (events.length === 0) return undefined;
    
    let state: UserAggregate = {
      userId: '',
      name: '',
      email: '',
      version: 0
    };
    
    for (const event of events) {
      if (event.eventType === 'UserCreated') {
        const payload = event.payload as UserCreated;
        state = {
          userId: payload.userId,
          name: payload.name,
          email: payload.email,
          version: event.version
        };
      } else if (event.eventType === 'UserUpdated') {
        const payload = event.payload as UserUpdated;
        state = {
          ...state,
          name: payload.name || state.name,
          email: payload.email || state.email,
          version: event.version
        };
      }
    }
    
    return state;
  }
}

describe('TestScenario DSL', () => {
  describe('Basic Scenarios', () => {
    it('should test command success scenario', async () => {
      const scenario = new TestScenario()
        .given('No previous events')
        .when<CreateUser>({
          type: 'CreateUser',
          payload: { name: 'John Doe', email: 'john@example.com' }
        })
        .then()
        .expectEvents([
          {
            eventType: 'UserCreated',
            payload: { userId: 'user-123', name: 'John Doe', email: 'john@example.com' }
          }
        ])
        .expectAggregateState<UserAggregate>({
          userId: 'user-123',
          name: 'John Doe',
          email: 'john@example.com',
          version: 1
        });

      const handler: ICommandHandler<CreateUser> = {
        handle: vi.fn().mockResolvedValue(ok([{
          eventType: 'UserCreated',
          payload: { userId: 'user-123', name: 'John Doe', email: 'john@example.com' }
        }]))
      };

      await scenario.run({
        commandHandler: handler,
        projector: new UserProjector()
      });

      expect(handler.handle).toHaveBeenCalledWith(
        expect.objectContaining({
          payload: { name: 'John Doe', email: 'john@example.com' }
        }),
        undefined
      );
    });

    it('should test command failure scenario', async () => {
      const scenario = new TestScenario()
        .given('No previous events')
        .when<CreateUser>({
          type: 'CreateUser',
          payload: { name: '', email: 'invalid-email' }
        })
        .then()
        .expectError('VALIDATION_ERROR', 'Invalid email format');

      const handler: ICommandHandler<CreateUser> = {
        handle: vi.fn().mockResolvedValue(
          err(new ValidationError('Invalid email format', 'email'))
        )
      };

      await scenario.run({
        commandHandler: handler,
        projector: new UserProjector()
      });
    });
  });

  describe('Given-When-Then with Events', () => {
    it('should test scenario with existing events', async () => {
      const partitionKeys = PartitionKeys.create('user-123', 'users');
      
      const scenario = new TestScenario()
        .givenEvents([
          {
            eventType: 'UserCreated',
            payload: { userId: 'user-123', name: 'John', email: 'john@example.com' },
            version: 1
          }
        ], partitionKeys)
        .when<UpdateUser>({
          type: 'UpdateUser',
          payload: { userId: 'user-123', name: 'John Doe' }
        })
        .then()
        .expectEvents([
          {
            eventType: 'UserUpdated',
            payload: { userId: 'user-123', name: 'John Doe' },
            version: 2
          }
        ])
        .expectAggregateState<UserAggregate>({
          userId: 'user-123',
          name: 'John Doe',
          email: 'john@example.com',
          version: 2
        });

      const handler: ICommandHandler<UpdateUser> = {
        handle: vi.fn().mockResolvedValue(ok([{
          eventType: 'UserUpdated',
          payload: { userId: 'user-123', name: 'John Doe' }
        }]))
      };

      await scenario.run({
        commandHandler: handler,
        projector: new UserProjector()
      });
    });
  });

  describe('Multiple Commands', () => {
    it('should test sequence of commands', async () => {
      const scenario = new TestScenario()
        .given('Empty aggregate')
        .whenSequence([
          {
            type: 'CreateUser',
            payload: { name: 'John', email: 'john@example.com' }
          },
          {
            type: 'UpdateUser',
            payload: { userId: 'user-123', name: 'John Doe' }
          }
        ])
        .then()
        .expectEventCount(2)
        .expectAggregateVersion(2)
        .expectAggregateState<UserAggregate>(state => {
          expect(state.name).toBe('John Doe');
          expect(state.email).toBe('john@example.com');
        });

      // Mock handlers that return appropriate events
      const createHandler: ICommandHandler<CreateUser> = {
        handle: vi.fn().mockResolvedValue(ok([{
          eventType: 'UserCreated',
          payload: { userId: 'user-123', name: 'John', email: 'john@example.com' }
        }]))
      };

      const updateHandler: ICommandHandler<UpdateUser> = {
        handle: vi.fn().mockResolvedValue(ok([{
          eventType: 'UserUpdated',
          payload: { userId: 'user-123', name: 'John Doe' }
        }]))
      };

      await scenario.runSequence({
        commandHandlers: {
          CreateUser: createHandler,
          UpdateUser: updateHandler
        },
        projector: new UserProjector()
      });
    });
  });

  describe('Event Assertions', () => {
    it('should support flexible event assertions', async () => {
      const scenario = new TestScenario()
        .given('No events')
        .when<CreateUser>({
          type: 'CreateUser',
          payload: { name: 'John', email: 'john@example.com' }
        })
        .then()
        .expectEvent(0, event => {
          expect(event.eventType).toBe('UserCreated');
          expect(event.version).toBe(1);
          expect(event.payload).toMatchObject({
            name: 'John',
            email: 'john@example.com'
          });
        })
        .expectNoMoreEvents();

      const handler: ICommandHandler<CreateUser> = {
        handle: vi.fn().mockResolvedValue(ok([{
          eventType: 'UserCreated',
          payload: { userId: 'user-123', name: 'John', email: 'john@example.com' }
        }]))
      };

      await scenario.run({
        commandHandler: handler,
        projector: new UserProjector()
      });
    });
  });

  describe('Time-based Scenarios', () => {
    it('should support time travel in scenarios', async () => {
      const baseTime = new Date('2024-01-01T00:00:00Z');
      
      const scenario = new TestScenario()
        .atTime(baseTime)
        .given('User created yesterday')
        .givenEvents([
          {
            eventType: 'UserCreated',
            payload: { userId: 'user-123', name: 'John', email: 'john@example.com' },
            version: 1,
            timestamp: new Date('2023-12-31T00:00:00Z')
          }
        ])
        .when<UpdateUser>({
          type: 'UpdateUser',
          payload: { userId: 'user-123', name: 'John Doe' }
        })
        .then()
        .expectEvents(events => {
          // Just verify we have the event
          expect(events).toHaveLength(1);
          expect(events[0].eventType).toBe('UserUpdated');
        });

      const handler: ICommandHandler<UpdateUser> = {
        handle: vi.fn().mockResolvedValue(ok([{
          eventType: 'UserUpdated',
          payload: { userId: 'user-123', name: 'John Doe' }
        }]))
      };

      await scenario.run({
        commandHandler: handler,
        projector: new UserProjector(),
        currentTime: baseTime
      });
    });
  });

  describe('Snapshot Scenarios', () => {
    it('should test with aggregate snapshot', async () => {
      const scenario = new TestScenario()
        .givenEvents([
          {
            eventType: 'UserCreated',
            payload: { userId: 'user-123', name: 'Initial User', email: 'initial@example.com' },
            version: 1
          }
        ])
        .when<UpdateUser>({
          type: 'UpdateUser',
          payload: { userId: 'user-123', email: 'new@example.com' }
        })
        .then()
        .expectAggregateVersion(2)
        .expectAggregateState<UserAggregate>({
          userId: 'user-123',
          name: 'Initial User',
          email: 'new@example.com',
          version: 2
        });

      const handler: ICommandHandler<UpdateUser> = {
        handle: vi.fn().mockResolvedValue(ok([{
          eventType: 'UserUpdated',
          payload: { userId: 'user-123', email: 'new@example.com' }
        }]))
      };

      await scenario.run({
        commandHandler: handler,
        projector: new UserProjector()
      });
    });
  });
});