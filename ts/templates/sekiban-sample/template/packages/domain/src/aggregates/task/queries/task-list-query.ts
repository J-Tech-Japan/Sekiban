import { z } from 'zod';
import { GetAllTasks } from './get-all-tasks.js';

// Re-export GetAllTasks as TaskListQuery for backward compatibility
export { GetAllTasks as TaskListQuery };

// Also export the schema for convenience
export const TaskListQuerySchema = z.object({
  status: z.enum(['active', 'completed', 'deleted']).optional(),
  limit: z.number().int().positive().max(100).default(50),
  offset: z.number().int().nonnegative().default(0)
});