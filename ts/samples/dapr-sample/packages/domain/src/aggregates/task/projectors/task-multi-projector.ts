import { IEvent } from '@sekiban/core';
import { z } from 'zod';
import { 
  TaskCreated, 
  TaskAssigned, 
  TaskCompleted, 
  TaskUpdated, 
  TaskDeleted,
  TaskCompletionReverted 
} from '../events/task-events.js';
import type { TaskState, CompletedTaskState } from './task-projector.js';

/**
 * Multi-projector for Task aggregate
 * Handles projections across multiple task instances
 */
export class TaskMultiProjector {
  // Multi-projector name used for actor ID
  static readonly multiProjectorName = 'TaskMultiProjector';
  
  // Get the multi-projector name
  get multiProjectorName() {
    return TaskMultiProjector.multiProjectorName;
  }
  
  // State: collection of all tasks
  private tasks: Map<string, TaskState | CompletedTaskState> = new Map();
  
  /**
   * Apply an event to the multi-projection
   */
  applyEvent(event: IEvent): void {
    const taskId = event.aggregateId;
    
    switch (event.eventType) {
      case 'TaskCreated': {
        const taskCreated = event.payload as z.infer<typeof TaskCreated.schema>;
        this.tasks.set(taskId, {
          aggregateType: 'Task',
          taskId: taskCreated.taskId,
          title: taskCreated.title,
          description: taskCreated.description,
          assignedTo: taskCreated.assignedTo,
          dueDate: taskCreated.dueDate,
          priority: taskCreated.priority,
          status: 'active',
          createdAt: taskCreated.createdAt,
          updatedAt: taskCreated.createdAt
        });
        break;
      }
      
      case 'TaskAssigned': {
        const task = this.tasks.get(taskId);
        if (task && task.aggregateType === 'Task') {
          const taskAssigned = event.payload as z.infer<typeof TaskAssigned.schema>;
          task.assignedTo = taskAssigned.assignedTo;
          task.updatedAt = taskAssigned.assignedAt;
        }
        break;
      }
      
      case 'TaskCompleted': {
        const task = this.tasks.get(taskId);
        if (task && task.aggregateType === 'Task') {
          const taskCompleted = event.payload as z.infer<typeof TaskCompleted.schema>;
          this.tasks.set(taskId, {
            ...task,
            aggregateType: 'CompletedTask',
            status: 'completed',
            completedAt: taskCompleted.completedAt,
            completedBy: taskCompleted.completedBy,
            completionNotes: taskCompleted.notes,
            updatedAt: taskCompleted.completedAt
          });
        }
        break;
      }
      
      case 'TaskUpdated': {
        const task = this.tasks.get(taskId);
        if (task && task.aggregateType === 'Task') {
          const taskUpdated = event.payload as z.infer<typeof TaskUpdated.schema>;
          if (taskUpdated.title !== undefined) task.title = taskUpdated.title;
          if (taskUpdated.description !== undefined) task.description = taskUpdated.description;
          if (taskUpdated.dueDate !== undefined) task.dueDate = taskUpdated.dueDate;
          if (taskUpdated.priority !== undefined) task.priority = taskUpdated.priority;
          task.updatedAt = taskUpdated.updatedAt;
        }
        break;
      }
      
      case 'TaskDeleted': {
        this.tasks.delete(taskId);
        break;
      }
      
      case 'TaskCompletionReverted': {
        const task = this.tasks.get(taskId);
        if (task && task.aggregateType === 'CompletedTask') {
          const taskReverted = event.payload as z.infer<typeof TaskCompletionReverted.schema>;
          this.tasks.set(taskId, {
            aggregateType: 'Task',
            taskId: task.taskId,
            title: task.title,
            description: task.description,
            assignedTo: task.assignedTo,
            dueDate: task.dueDate,
            priority: task.priority,
            status: 'active',
            createdAt: task.createdAt,
            updatedAt: taskReverted.revertedAt
          });
        }
        break;
      }
    }
  }
  
  /**
   * Get all tasks
   */
  getAllTasks(options?: { status?: string; limit?: number; offset?: number }): (TaskState | CompletedTaskState)[] {
    let tasks = Array.from(this.tasks.values());
    
    // Filter by status if specified
    if (options?.status) {
      tasks = tasks.filter(task => {
        if (options.status === 'completed') {
          return task.aggregateType === 'CompletedTask';
        }
        if (options.status === 'active' || options.status === 'deleted') {
          return task.aggregateType === 'Task' && task.status === options.status;
        }
        return false;
      });
    }
    
    // Apply pagination
    const offset = options?.offset || 0;
    const limit = options?.limit || 50;
    
    return tasks.slice(offset, offset + limit);
  }
  
  /**
   * Get total count
   */
  getTotalCount(options?: { status?: string }): number {
    if (!options?.status) {
      return this.tasks.size;
    }
    
    return Array.from(this.tasks.values()).filter(task => {
      if (options.status === 'completed') {
        return task.aggregateType === 'CompletedTask';
      }
      if (options.status === 'active' || options.status === 'deleted') {
        return task.aggregateType === 'Task' && task.status === options.status;
      }
      return false;
    }).length;
  }
  
  /**
   * Handle GetAllTasks query
   */
  handleGetAllTasks(query: any): (TaskState | CompletedTaskState)[] {
    // Extract filter options from query
    const options = {
      status: query.status,
      limit: query.limit,
      offset: query.offset
    };
    
    return this.getAllTasks(options);
  }
}