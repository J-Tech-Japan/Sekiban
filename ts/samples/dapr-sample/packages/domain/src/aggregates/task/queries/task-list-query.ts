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
   * Filter tasks from the projection
   */
  static handleFilter(
    projection: MultiProjectionState<AggregateListProjector<TaskProjector>>,
    query: TaskListQuery,
    context: IQueryContext
  ): Result<TaskListResponse[], never> {
    const tasks = projection.payload.getAggregates()
      .filter(aggregate => {
        const payload = aggregate.payload;
        return (
          payload && 
          typeof payload === 'object' && 
          'aggregateType' in payload &&
          (payload.aggregateType === 'Task' || payload.aggregateType === 'CompletedTask')
        );
      })
      .map(aggregate => {
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
      });
    
    return ok(tasks);
  }
  
  /**
   * Sort tasks by creation date
   */
  static handleSort(
    filteredList: TaskListResponse[],
    query: TaskListQuery,
    context: IQueryContext
  ): Result<TaskListResponse[], never> {
    const sorted = [...filteredList].sort((a, b) => 
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
    );
    return ok(sorted);
  }
}

/**
 * Query to get active tasks only
 */
export class ActiveTaskListQuery implements IMultiProjectionListQuery<
  AggregateListProjector<TaskProjector>,
  ActiveTaskListQuery,
  TaskListResponse
> {
  /**
   * Filter active tasks from the projection
   */
  static handleFilter(
    projection: MultiProjectionState<AggregateListProjector<TaskProjector>>,
    query: ActiveTaskListQuery,
    context: IQueryContext
  ): Result<TaskListResponse[], never> {
    const tasks = projection.payload.getAggregates()
      .filter(aggregate => {
        const payload = aggregate.payload;
        return (
          payload && 
          typeof payload === 'object' && 
          'aggregateType' in payload &&
          payload.aggregateType === 'Task' &&
          (payload as TaskState).status === 'active'
        );
      })
      .map(aggregate => {
        const payload = aggregate.payload as TaskState;
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
      });
    
    return ok(tasks);
  }
  
  /**
   * Sort tasks by priority and then by creation date
   */
  static handleSort(
    filteredList: TaskListResponse[],
    query: ActiveTaskListQuery,
    context: IQueryContext
  ): Result<TaskListResponse[], never> {
    const priorityOrder = { high: 0, medium: 1, low: 2 };
    const sorted = [...filteredList].sort((a, b) => {
      const priorityDiff = priorityOrder[a.priority] - priorityOrder[b.priority];
      if (priorityDiff !== 0) return priorityDiff;
      return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
    });
    return ok(sorted);
  }
}

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
   * Filter tasks by assignee
   */
  static handleFilter(
    projection: MultiProjectionState<AggregateListProjector<TaskProjector>>,
    query: TasksByAssigneeQuery,
    context: IQueryContext
  ): Result<TaskListResponse[], never> {
    const tasks = projection.payload.getAggregates()
      .filter(aggregate => {
        const payload = aggregate.payload;
        return (
          payload && 
          typeof payload === 'object' && 
          'aggregateType' in payload &&
          (payload.aggregateType === 'Task' || payload.aggregateType === 'CompletedTask') &&
          (payload as TaskState | CompletedTaskState).assignedTo === query.assigneeId
        );
      })
      .map(aggregate => {
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
      });
    
    return ok(tasks);
  }
  
  /**
   * Sort tasks by due date
   */
  static handleSort(
    filteredList: TaskListResponse[],
    query: TasksByAssigneeQuery,
    context: IQueryContext
  ): Result<TaskListResponse[], never> {
    const sorted = [...filteredList].sort((a, b) => {
      if (!a.dueDate && !b.dueDate) return 0;
      if (!a.dueDate) return 1;
      if (!b.dueDate) return -1;
      return new Date(a.dueDate).getTime() - new Date(b.dueDate).getTime();
    });
    return ok(sorted);
  }
}