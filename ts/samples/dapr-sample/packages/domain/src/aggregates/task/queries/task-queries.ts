import { z } from 'zod';
import { PartitionKeys } from '@sekiban/core';
import type { TaskState } from '../projectors/task-projector.js';
import { TaskProjector } from '../projectors/task-projector.js';

// Export GetAllTasks query
export { GetAllTasks } from './get-all-tasks.js';

// Define the query schema
const GetTaskByIdSchema = z.object({
  taskId: z.string().uuid()
});

// Create a simple query class for getting task by ID
export class GetTaskById {
  constructor(private data: z.infer<typeof GetTaskByIdSchema>) {}

  static create(data: z.infer<typeof GetTaskByIdSchema>) {
    // Validate the data
    const parsed = GetTaskByIdSchema.parse(data);
    return new GetTaskById(parsed);
  }

  getAggregateType() {
    return 'Task';
  }

  getProjector() {
    return new TaskProjector();
  }

  getPartitionKeys() {
    return PartitionKeys.existing(this.data.taskId, 'Task');
  }

  get taskId() {
    return this.data.taskId;
  }
}