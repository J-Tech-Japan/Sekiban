import { defineProjector, EmptyAggregatePayload, type ITypedAggregatePayload } from '@sekiban/core';
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

// Define the User projector
export const UserProjector = defineProjector({
  aggregateType: 'User',
  initialState: () => new EmptyAggregatePayload(),
  projections: {
    [UserCreated.type]: (state, event) => {
      const userState: UserState = {
        aggregateType: 'User',
        userId: event.payload.userId,
        name: event.payload.name,
        email: event.payload.email,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      return userState;
    },
    [UserNameChanged.type]: (state, event) => {
      return {
        ...state,
        name: event.payload.newName,
        updatedAt: new Date().toISOString()
      };
    },
    [UserEmailChanged.type]: (state, event) => {
      return {
        ...state,
        email: event.payload.newEmail,
        updatedAt: new Date().toISOString()
      };
    }
  }
});