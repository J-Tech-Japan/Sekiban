import { z } from 'zod';
import { TaskMultiProjector } from '../projectors/task-multi-projector.js';

// Define the query schema
const GetAllTasksSchema = z.object({
  status: z.enum(['active', 'completed', 'deleted']).optional(),
  limit: z.number().int().positive().max(100).default(50),
  offset: z.number().int().nonnegative().default(0)
});

/**
 * Query to get all tasks with optional filtering
 */
export class GetAllTasks {
  constructor(private data: z.infer<typeof GetAllTasksSchema>) {}

  static create(data: Partial<z.infer<typeof GetAllTasksSchema>> = {}) {
    // Apply defaults and validate
    const parsed = GetAllTasksSchema.parse(data);
    return new GetAllTasks(parsed);
  }

  getMultiProjector() {
    return TaskMultiProjector;
  }

  get status() {
    return this.data.status;
  }

  get limit() {
    return this.data.limit;
  }

  get offset() {
    return this.data.offset;
  }
}