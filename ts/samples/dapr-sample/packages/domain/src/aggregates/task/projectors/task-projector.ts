import { defineProjector, EmptyAggregatePayload } from '@sekiban/core';
import { z } from 'zod';
import { 
  TaskCreated, 
  TaskAssigned, 
  TaskCompleted, 
  TaskUpdated, 
  TaskDeleted 
} from '../events/task-events.js';

export interface TaskState {
  aggregateType: 'Task';
  taskId: string;
  title: string;
  description?: string;
  assignedTo?: string;
  dueDate?: string;
  priority: 'low' | 'medium' | 'high';
  status: 'active' | 'completed' | 'deleted';
  createdAt: string;
  updatedAt: string;
  completedAt?: string;
  deletedAt?: string;
}

// Task Projector
export const TaskProjector = defineProjector<TaskState>({
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
      if (!state || state.aggregateType !== 'Task') return state;
      return {
        ...state,
        status: 'completed' as const,
        completedAt: event.completedAt,
        updatedAt: event.completedAt
      } as TaskState;
    },
    
    TaskUpdated: (state: any, event: z.infer<typeof TaskUpdated.schema>) => {
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
      if (!state || state.aggregateType !== 'Task') return state;
      return {
        ...state,
        status: 'deleted' as const,
        deletedAt: event.deletedAt,
        updatedAt: event.deletedAt
      } as TaskState;
    }
  }
});