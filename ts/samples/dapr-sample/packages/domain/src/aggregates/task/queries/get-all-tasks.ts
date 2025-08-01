import { z } from 'zod';
import { TaskMultiProjector } from '../projectors/task-multi-projector.js';

// Define the query schema (no parameters needed for getting all tasks)
const GetAllTasksSchema = z.object({
  // Optional filters can be added here later
  status: z.enum(['active', 'completed', 'deleted']).optional(),
  limit: z.number().min(1).max(100).default(50).optional(),
  offset: z.number().min(0).default(0).optional()
});

// Create a query class for getting all tasks
export class GetAllTasks {
  constructor(private data: z.infer<typeof GetAllTasksSchema> = {}) {}

  static create(data: z.infer<typeof GetAllTasksSchema> = {}) {
    // Validate the data
    const parsed = GetAllTasksSchema.parse(data);
    return new GetAllTasks(parsed);
  }

  getAggregateType() {
    return 'Task';
  }

  getProjector() {
    // Return the multi-projector with multiProjectorName property
    return {
      multiProjectorName: TaskMultiProjector.multiProjectorName
    };
  }

  // Multi-partition query - no specific partition keys
  getPartitionKeys() {
    return null; // This indicates we want all partitions
  }
  
  // Mark this as a multi-projection query
  get isMultiProjection() {
    return true;
  }

  get status() {
    return this.data.status;
  }

  get limit() {
    return this.data.limit || 50;
  }

  get offset() {
    return this.data.offset || 0;
  }
}