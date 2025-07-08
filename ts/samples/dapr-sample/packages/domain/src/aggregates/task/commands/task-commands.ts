import { z } from 'zod';
import { command, PartitionKeys } from '@sekiban/core';
import { TaskProjector } from '../projectors/task-projector.js';
import { TaskCreated, TaskAssigned, TaskCompleted, TaskUpdated, TaskDeleted } from '../events/task-events.js';

// Create Task Command
export const CreateTask = command.create('CreateTask', {
  schema: z.object({
    title: z.string().min(1).max(200),
    description: z.string().max(1000).optional(),
    assignedTo: z.string().email().optional(),
    dueDate: z.string().datetime().optional(),
    priority: z.enum(['low', 'medium', 'high']).default('medium')
  }),
  projector: new TaskProjector(),
  partitionKeys: () => PartitionKeys.generate('Task'),
  handle: (data, { aggregateId, appendEvent }) => {
    appendEvent(TaskCreated.create({
      taskId: aggregateId,
      title: data.title,
      description: data.description,
      assignedTo: data.assignedTo,
      dueDate: data.dueDate,
      priority: data.priority,
      createdAt: new Date().toISOString()
    }));
  }
});

// Assign Task Command
export const AssignTask = command.update('AssignTask', {
  schema: z.object({
    taskId: z.string().uuid(),
    assignedTo: z.string().email()
  }),
  projector: new TaskProjector(),
  partitionKeys: (data) => PartitionKeys.existing(data.taskId, 'Task'),
  handle: (data, { appendEvent }) => {
    appendEvent(TaskAssigned.create({
      taskId: data.taskId,
      assignedTo: data.assignedTo,
      assignedAt: new Date().toISOString()
    }));
  }
});

// Complete Task Command
export const CompleteTask = command.update('CompleteTask', {
  schema: z.object({
    taskId: z.string().uuid(),
    completedBy: z.string().email(),
    notes: z.string().max(500).optional()
  }),
  projector: new TaskProjector(),
  partitionKeys: (data) => PartitionKeys.existing(data.taskId, 'Task'),
  handle: (data, { appendEvent }) => {
    appendEvent(TaskCompleted.create({
      taskId: data.taskId,
      completedBy: data.completedBy,
      notes: data.notes,
      completedAt: new Date().toISOString()
    }));
  }
});

// Update Task Command  
export const UpdateTask = command.update('UpdateTask', {
  schema: z.object({
    taskId: z.string().uuid(),
    title: z.string().min(1).max(200).optional(),
    description: z.string().max(1000).optional(),
    dueDate: z.string().datetime().optional(),
    priority: z.enum(['low', 'medium', 'high']).optional()
  }),
  projector: new TaskProjector(),
  partitionKeys: (data) => PartitionKeys.existing(data.taskId, 'Task'),
  handle: (data, { appendEvent }) => {
    appendEvent(TaskUpdated.create({
      taskId: data.taskId,
      title: data.title,
      description: data.description,
      dueDate: data.dueDate,
      priority: data.priority,
      updatedAt: new Date().toISOString()
    }));
  }
});

// Delete Task Command
export const DeleteTask = command.update('DeleteTask', {
  schema: z.object({
    taskId: z.string().uuid(),
    deletedBy: z.string().email(),
    reason: z.string().max(200).optional()
  }),
  projector: new TaskProjector(),
  partitionKeys: (data) => PartitionKeys.existing(data.taskId, 'Task'),
  handle: (data, { appendEvent }) => {
    appendEvent(TaskDeleted.create({
      taskId: data.taskId,
      deletedBy: data.deletedBy,
      reason: data.reason,
      deletedAt: new Date().toISOString()
    }));
  }
});