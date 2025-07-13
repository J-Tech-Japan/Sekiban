import { defineProjector } from '@sekiban/core';
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

export type UserState = z.infer<typeof userStateSchema>;

// Define the User projector
export const UserProjector = defineProjector({
  aggregateType: 'User',
  projections: {
    [UserCreated.type]: {
      aggregate: { _empty: true },
      handler: (aggregate, event) => {
        const userState: UserState = {
          aggregateType: 'User',
          userId: event.payload.userId,
          name: event.payload.name,
          email: event.payload.email,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString()
        };
        return userState;
      }
    },
    [UserNameChanged.type]: {
      aggregate: userStateSchema,
      handler: (aggregate, event) => {
        return {
          ...aggregate,
          name: event.payload.newName,
          updatedAt: new Date().toISOString()
        };
      }
    },
    [UserEmailChanged.type]: {
      aggregate: userStateSchema,
      handler: (aggregate, event) => {
        return {
          ...aggregate,
          email: event.payload.newEmail,
          updatedAt: new Date().toISOString()
        };
      }
    }
  }
});