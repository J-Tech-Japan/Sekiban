import { z } from 'zod';
import { defineEvent } from '@sekiban/core';

export const TaskCreated = defineEvent({
  type: 'TaskCreated',
  schema: z.object({
    taskId: z.string().uuid(),
    title: z.string().min(1).max(200),
    description: z.string().max(1000).optional(),
    assignedTo: z.string().email().optional(),
    dueDate: z.string().datetime().optional(),
    priority: z.enum(['low', 'medium', 'high']).default('medium'),
    createdAt: z.string().datetime()
  })
});

export const TaskAssigned = defineEvent({
  type: 'TaskAssigned',
  schema: z.object({
    taskId: z.string().uuid(),
    assignedTo: z.string().email(),
    assignedAt: z.string().datetime()
  })
});

export const TaskCompleted = defineEvent({
  type: 'TaskCompleted',
  schema: z.object({
    taskId: z.string().uuid(),
    completedBy: z.string().email(),
    completedAt: z.string().datetime(),
    notes: z.string().max(500).optional()
  })
});

export const TaskUpdated = defineEvent({
  type: 'TaskUpdated',
  schema: z.object({
    taskId: z.string().uuid(),
    title: z.string().min(1).max(200).optional(),
    description: z.string().max(1000).optional(),
    dueDate: z.string().datetime().optional(),
    priority: z.enum(['low', 'medium', 'high']).optional(),
    updatedAt: z.string().datetime()
  })
});

export const TaskDeleted = defineEvent({
  type: 'TaskDeleted',
  schema: z.object({
    taskId: z.string().uuid(),
    deletedBy: z.string().email(),
    deletedAt: z.string().datetime(),
    reason: z.string().max(200).optional()
  })
});

export const TaskCompletionReverted = defineEvent({
  type: 'TaskCompletionReverted',
  schema: z.object({
    taskId: z.string().uuid(),
    revertedBy: z.string().email(),
    revertedAt: z.string().datetime(),
    reason: z.string().max(500).optional()
  })
});