import { 
  IMultiProjectionListQuery,
  AggregateListProjector,
  MultiProjectionState,
  IQueryContext,
  Result,
  ok
} from '@sekiban/core';
import { TaskProjector } from '../projectors/task-projector.js';
import { TaskState, CompletedTaskState } from '../projectors/task-projector.js';

/**
 * Response type for task list query
 */
export interface TaskListResponse {
  taskId: string;
  title: string;
  description?: string;
  assignedTo?: string;
  dueDate?: string;
  priority: 'low' | 'medium' | 'high';
  status: 'active' | 'completed' | 'deleted';
  createdAt: string;
  updatedAt: string;
}

/**
 * Query to get all tasks
 */
export class TaskListQuery implements IMultiProjectionListQuery<
  AggregateListProjector<TaskProjector>,
  TaskListQuery,
  TaskListResponse
> {
  /**
   * Get the aggregate type for this query
   */
  getAggregateType(): string {
    return 'Task';
  }
  
  /**
   * Get the projector for this query
   */
  getProjector() {
    return new TaskProjector();
  }
  
  /**
   * Get the multi-projector name for this query
   */
  getMultiProjectorName(): string {
    return AggregateListProjector.getMultiProjectorName(() => new TaskProjector());
  }
  
  /**
   * Filter tasks from the aggregate
   */
  handleFilter(aggregate: any): boolean {
    const payload = aggregate.payload;
    return (
      payload && 
      typeof payload === 'object' && 
      'aggregateType' in payload &&
      (payload.aggregateType === 'Task' || payload.aggregateType === 'CompletedTask')
    );
  }
  
  /**
   * Sort tasks by creation date (newest first)
   */
  handleSort(a: any, b: any): number {
    const payloadA = a.payload as TaskState | CompletedTaskState;
    const payloadB = b.payload as TaskState | CompletedTaskState;
    return new Date(payloadB.createdAt).getTime() - new Date(payloadA.createdAt).getTime();
  }
  
  /**
   * Transform aggregate to response format
   */
  transformToResponse(aggregate: any): TaskListResponse {
    const payload = aggregate.payload as TaskState | CompletedTaskState;
    return {
      taskId: payload.taskId,
      title: payload.title,
      description: payload.description,
      assignedTo: payload.assignedTo,
      dueDate: payload.dueDate,
      priority: payload.priority,
      status: payload.status,
      createdAt: payload.createdAt,
      updatedAt: payload.updatedAt
    };
  }
}