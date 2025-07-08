import { 
  defineProjector, 
  EmptyAggregatePayload, 
  AggregateProjector, 
  PartitionKeys, 
  Aggregate, 
  IEvent, 
  Result, 
  ok, 
  SekibanError 
} from '@sekiban/core';
import { z } from 'zod';
import { 
  TaskCreated, 
  TaskAssigned, 
  TaskCompleted, 
  TaskUpdated, 
  TaskDeleted,
  TaskCompletionReverted
} from '../events/task-events.js';

// Active task state
export interface TaskState {
  aggregateType: 'Task';
  taskId: string;
  title: string;
  description?: string;
  assignedTo?: string;
  dueDate?: string;
  priority: 'low' | 'medium' | 'high';
  status: 'active' | 'deleted';
  createdAt: string;
  updatedAt: string;
  deletedAt?: string;
}

// Completed task state - separate type to enforce state machine
export interface CompletedTaskState {
  aggregateType: 'CompletedTask';
  taskId: string;
  title: string;
  description?: string;
  assignedTo?: string;
  dueDate?: string;
  priority: 'low' | 'medium' | 'high';
  status: 'completed';
  createdAt: string;
  updatedAt: string;
  completedAt: string;
  completedBy: string;
  completionNotes?: string;
}

// Union type for all possible states
export type TaskPayloadUnion = TaskState | CompletedTaskState;

// Task Projector using defineProjector - now supports multiple states
export const taskProjectorDefinition = defineProjector<TaskPayloadUnion>({
  aggregateType: 'Task',
  
  initialState: () => new EmptyAggregatePayload(),
  
  projections: {
    TaskCreated: (state: any, event: z.infer<typeof TaskCreated.schema>) => ({
      aggregateType: 'Task' as const,
      taskId: event.taskId,
      title: event.title,
      description: event.description,
      assignedTo: event.assignedTo,
      dueDate: event.dueDate,
      priority: event.priority,
      status: 'active' as const,
      createdAt: event.createdAt,
      updatedAt: event.createdAt
    } as TaskState),
    
    TaskAssigned: (state: any, event: z.infer<typeof TaskAssigned.schema>) => {
      if (!state || state.aggregateType !== 'Task') return state;
      return {
        ...state,
        assignedTo: event.assignedTo,
        updatedAt: event.assignedAt
      } as TaskState;
    },
    
    TaskCompleted: (state: any, event: z.infer<typeof TaskCompleted.schema>) => {
      // Can only complete active tasks
      if (!state || state.aggregateType !== 'Task' || state.status === 'deleted') return state;
      
      // Transform to CompletedTaskState
      const taskState = state as TaskState;
      return {
        aggregateType: 'CompletedTask' as const,
        taskId: taskState.taskId,
        title: taskState.title,
        description: taskState.description,
        assignedTo: taskState.assignedTo,
        dueDate: taskState.dueDate,
        priority: taskState.priority,
        status: 'completed' as const,
        createdAt: taskState.createdAt,
        updatedAt: event.completedAt,
        completedAt: event.completedAt,
        completedBy: event.completedBy,
        completionNotes: event.notes
      } as CompletedTaskState;
    },
    
    TaskUpdated: (state: any, event: z.infer<typeof TaskUpdated.schema>) => {
      // Can only update active tasks
      if (!state || state.aggregateType !== 'Task') return state;
      return {
        ...state,
        title: event.title || state.title,
        description: event.description !== undefined ? event.description : state.description,
        dueDate: event.dueDate !== undefined ? event.dueDate : state.dueDate,
        priority: event.priority || state.priority,
        updatedAt: event.updatedAt
      } as TaskState;
    },
    
    TaskDeleted: (state: any, event: z.infer<typeof TaskDeleted.schema>) => {
      // Can only delete active tasks
      if (!state || state.aggregateType !== 'Task') return state;
      return {
        ...state,
        status: 'deleted' as const,
        deletedAt: event.deletedAt,
        updatedAt: event.deletedAt
      } as TaskState;
    },
    
    TaskCompletionReverted: (state: any, event: z.infer<typeof TaskCompletionReverted.schema>) => {
      // Can only revert completed tasks
      if (!state || state.aggregateType !== 'CompletedTask') return state;
      
      // Transform back to TaskState
      const completedState = state as CompletedTaskState;
      return {
        aggregateType: 'Task' as const,
        taskId: completedState.taskId,
        title: completedState.title,
        description: completedState.description,
        assignedTo: completedState.assignedTo,
        dueDate: completedState.dueDate,
        priority: completedState.priority,
        status: 'active' as const,
        createdAt: completedState.createdAt,
        updatedAt: event.revertedAt
      } as TaskState;
    }
  }
});

// TaskProjector class for command API compatibility
export class TaskProjector extends AggregateProjector<TaskPayloadUnion> {
  readonly aggregateTypeName = 'Task';
  
  getInitialState(partitionKeys: PartitionKeys): Aggregate<EmptyAggregatePayload> {
    return taskProjectorDefinition.getInitialState(partitionKeys);
  }
  
  project(
    aggregate: Aggregate<TaskPayloadUnion | EmptyAggregatePayload>, 
    event: IEvent
  ): Result<Aggregate<TaskPayloadUnion | EmptyAggregatePayload>, SekibanError> {
    return taskProjectorDefinition.project(aggregate, event);
  }
  
  canHandle(eventType: string): boolean {
    return [
      'TaskCreated',
      'TaskAssigned',
      'TaskCompleted',
      'TaskUpdated',
      'TaskDeleted',
      'TaskCompletionReverted'
    ].includes(eventType);
  }
  
  getSupportedPayloadTypes(): string[] {
    return ['Task', 'CompletedTask'];
  }
}