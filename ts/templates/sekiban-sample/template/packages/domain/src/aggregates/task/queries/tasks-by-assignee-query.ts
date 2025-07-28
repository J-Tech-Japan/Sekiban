import { 
  IMultiProjectionListQuery,
  AggregateListProjector
} from '@sekiban/core';
import { TaskProjector } from '../projectors/task-projector.js';
import { TaskState, CompletedTaskState } from '../projectors/task-projector.js';
import { TaskListResponse } from './task-list-query.js';

/**
 * Query to get tasks assigned to a specific user
 */
export class TasksByAssigneeQuery implements IMultiProjectionListQuery<
  AggregateListProjector<TaskProjector>,
  TasksByAssigneeQuery,
  TaskListResponse
> {
  constructor(public readonly assigneeId: string) {}
  
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
   * Filter tasks by assignee
   */
  handleFilter(aggregate: any): boolean {
    const payload = aggregate.payload;
    return (
      payload && 
      typeof payload === 'object' && 
      'aggregateType' in payload &&
      (payload.aggregateType === 'Task' || payload.aggregateType === 'CompletedTask') &&
      (payload as TaskState | CompletedTaskState).assignedTo === this.assigneeId
    );
  }
  
  /**
   * Sort tasks by due date
   */
  handleSort(a: any, b: any): number {
    const payloadA = a.payload as TaskState | CompletedTaskState;
    const payloadB = b.payload as TaskState | CompletedTaskState;
    if (!payloadA.dueDate && !payloadB.dueDate) return 0;
    if (!payloadA.dueDate) return 1;
    if (!payloadB.dueDate) return -1;
    return new Date(payloadA.dueDate).getTime() - new Date(payloadB.dueDate).getTime();
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