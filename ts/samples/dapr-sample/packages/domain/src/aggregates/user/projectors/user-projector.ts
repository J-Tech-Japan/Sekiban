import { 
  defineProjector, 
  EmptyAggregatePayload, 
  type ITypedAggregatePayload,
  AggregateProjector,
  PartitionKeys,
  Aggregate,
  IEvent,
  Result,
  SekibanError
} from '@sekiban/core';
import { z } from 'zod';
import { UserCreated, UserNameChanged, UserEmailChanged } from '../events/index.js';

// User state schema
const userStateSchema = z.object({
  aggregateType: z.literal('User'),
  userId: z.string().uuid(),
  name: z.string(),
  email: z.string().email(),
  createdAt: z.string(),
  updatedAt: z.string()
});

export interface UserState extends ITypedAggregatePayload {
  readonly aggregateType: 'User';
  userId: string;
  name: string;
  email: string;
  createdAt: string;
  updatedAt: string;
}

// Define the User projector using defineProjector
export const userProjectorDefinition = defineProjector<UserState>({
  aggregateType: 'User',
  initialState: () => new EmptyAggregatePayload(),
  projections: {
    UserCreated: (state: any, event: z.infer<typeof UserCreated.schema>) => ({
      aggregateType: 'User' as const,
      userId: event.userId,
      name: event.name,
      email: event.email,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString()
    } as UserState),
    
    UserNameChanged: (state: any, event: z.infer<typeof UserNameChanged.schema>) => {
      if (!state || state.aggregateType !== 'User') return state;
      return {
        ...state,
        name: event.newName,
        updatedAt: new Date().toISOString()
      } as UserState;
    },
    
    UserEmailChanged: (state: any, event: z.infer<typeof UserEmailChanged.schema>) => {
      if (!state || state.aggregateType !== 'User') return state;
      return {
        ...state,
        email: event.newEmail,
        updatedAt: new Date().toISOString()
      } as UserState;
    }
  }
});

// UserProjector class for command API compatibility
export class UserProjector extends AggregateProjector<UserState> {
  readonly aggregateTypeName = 'User';
  
  getInitialState(partitionKeys: PartitionKeys): Aggregate<EmptyAggregatePayload> {
    return userProjectorDefinition.getInitialState(partitionKeys);
  }
  
  project(
    aggregate: Aggregate<UserState | EmptyAggregatePayload>, 
    event: IEvent
  ): Result<Aggregate<UserState | EmptyAggregatePayload>, SekibanError> {
    return userProjectorDefinition.project(aggregate, event);
  }
  
  canHandle(eventType: string): boolean {
    return [
      'UserCreated',
      'UserNameChanged', 
      'UserEmailChanged'
    ].includes(eventType);
  }
  
  getSupportedPayloadTypes(): string[] {
    return ['User'];
  }
}