import { z } from 'zod';
import { defineEvent } from '@sekiban/core';

// User Created Event
export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string().uuid(),
    name: z.string().min(1),
    email: z.string().email()
  })
});

// User Name Changed Event
export const UserNameChanged = defineEvent({
  type: 'UserNameChanged',
  schema: z.object({
    userId: z.string().uuid(),
    newName: z.string().min(1)
  })
});

// User Email Changed Event
export const UserEmailChanged = defineEvent({
  type: 'UserEmailChanged',
  schema: z.object({
    userId: z.string().uuid(),
    newEmail: z.string().email()
  })
});

export type UserCreatedType = z.infer<typeof UserCreated.schema>;
export type UserNameChangedType = z.infer<typeof UserNameChanged.schema>;
export type UserEmailChangedType = z.infer<typeof UserEmailChanged.schema>;