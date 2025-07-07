import { z } from 'zod';
import { defineCommand, PartitionKeys, ok, err, EmptyAggregatePayload } from '@sekiban/core';
import { TaskCreated, TaskAssigned, TaskCompleted, TaskUpdated, TaskDeleted } from '../events/task-events.js';
import type { TaskState } from '../projectors/task-projector.js';
import { TaskProjector } from '../projectors/task-projector.js';

// Define schema types
const CreateTaskSchema = z.object({
  title: z.string().min(1).max(200),
  description: z.string().max(1000).optional(),
  assignedTo: z.string().email().optional(),
  dueDate: z.string().datetime().optional(),
  priority: z.enum(['low', 'medium', 'high']).default('medium')
});

const AssignTaskSchema = z.object({
  taskId: z.string().uuid(),
  assignedTo: z.string().email()
});

const CompleteTaskSchema = z.object({
  taskId: z.string().uuid(),
  completedBy: z.string().email(),
  notes: z.string().max(500).optional()
});

const UpdateTaskSchema = z.object({
  taskId: z.string().uuid(),
  title: z.string().min(1).max(200).optional(),
  description: z.string().max(1000).optional(),
  dueDate: z.string().datetime().optional(),
  priority: z.enum(['low', 'medium', 'high']).optional()
});

const DeleteTaskSchema = z.object({
  taskId: z.string().uuid(),
  deletedBy: z.string().email(),
  reason: z.string().max(200).optional()
});

// Helper function to create validation error
const createValidationError = (type: string, errors: string[]) => err({
  code: 'COMMAND_VALIDATION_ERROR',
  commandType: type,
  validationErrors: errors,
  message: `Command validation failed for ${type}: ${errors.join(', ')}`,
  name: 'CommandValidationError'
} as any);

export const CreateTask = defineCommand({
  type: 'CreateTask',
  schema: CreateTaskSchema,
  aggregateType: 'Task',
  projector: TaskProjector,
  handlers: {
    specifyPartitionKeys: (data: z.infer<typeof CreateTaskSchema>) => PartitionKeys.generate('Task'),
    validate: (data: z.infer<typeof CreateTaskSchema>) => {
      // Business validation
      if (data.dueDate && new Date(data.dueDate) < new Date()) {
        return createValidationError('CreateTask', ['Due date cannot be in the past']);
      }
      return ok(undefined);
    },
    handle: (data: z.infer<typeof CreateTaskSchema>, context: any) => {
      const aggregateResult = context.getAggregate();
      if (aggregateResult.isErr()) {
        return err(aggregateResult.error);
      }
      
      const aggregate = aggregateResult.value;
      if (aggregate.payload && aggregate.payload.aggregateType !== 'Empty') {
        return createValidationError('CreateTask', ['Task already exists']);
      }
      
      return ok([
        TaskCreated.create({
          taskId: globalThis.crypto?.randomUUID?.() || Math.random().toString(36).substring(2, 15),
          title: data.title,
          description: data.description,
          assignedTo: data.assignedTo,
          dueDate: data.dueDate,
          priority: data.priority,
          createdAt: new Date().toISOString()
        })
      ]);
    }
  }
});

export const AssignTask = defineCommand({
  type: 'AssignTask',
  schema: AssignTaskSchema,
  aggregateType: 'Task',
  projector: TaskProjector,
  handlers: {
    specifyPartitionKeys: (data: z.infer<typeof AssignTaskSchema>) => PartitionKeys.existing('Task', data.taskId),
    validate: (data: z.infer<typeof AssignTaskSchema>) => ok(undefined),
    handle: (data: z.infer<typeof AssignTaskSchema>, context: any) => {
      const aggregateResult = context.getAggregate();
      if (aggregateResult.isErr()) {
        return err(aggregateResult.error);
      }
      
      const aggregate = aggregateResult.value;
      if (!aggregate.payload || aggregate.payload.aggregateType === 'Empty') {
        return createValidationError('AssignTask', ['Task not found']);
      }
      
      const task = aggregate.payload as TaskState;
      if (task.status === 'completed') {
        return createValidationError('AssignTask', ['Cannot assign a completed task']);
      }
      
      return ok([
        TaskAssigned.create({
          taskId: data.taskId,
          assignedTo: data.assignedTo,
          assignedAt: new Date().toISOString()
        })
      ]);
    }
  }
});

export const CompleteTask = defineCommand({
  type: 'CompleteTask',
  schema: CompleteTaskSchema,
  aggregateType: 'Task',
  projector: TaskProjector,
  handlers: {
    specifyPartitionKeys: (data: z.infer<typeof CompleteTaskSchema>) => PartitionKeys.existing('Task', data.taskId),
    validate: (data: z.infer<typeof CompleteTaskSchema>) => ok(undefined),
    handle: (data: z.infer<typeof CompleteTaskSchema>, context: any) => {
      const aggregateResult = context.getAggregate();
      if (aggregateResult.isErr()) {
        return err(aggregateResult.error);
      }
      
      const aggregate = aggregateResult.value;
      if (!aggregate.payload || aggregate.payload.aggregateType === 'Empty') {
        return createValidationError('CompleteTask', ['Task not found']);
      }
      
      const task = aggregate.payload as TaskState;
      if (task.status === 'completed') {
        return createValidationError('CompleteTask', ['Task is already completed']);
      }
      
      return ok([
        TaskCompleted.create({
          taskId: data.taskId,
          completedBy: data.completedBy,
          completedAt: new Date().toISOString(),
          notes: data.notes
        })
      ]);
    }
  }
});

export const UpdateTask = defineCommand({
  type: 'UpdateTask',
  schema: UpdateTaskSchema,
  aggregateType: 'Task',
  projector: TaskProjector,
  handlers: {
    specifyPartitionKeys: (data: z.infer<typeof UpdateTaskSchema>) => PartitionKeys.existing('Task', data.taskId),
    validate: (data: z.infer<typeof UpdateTaskSchema>) => {
      // Business validation
      if (data.dueDate && new Date(data.dueDate) < new Date()) {
        return createValidationError('UpdateTask', ['Due date cannot be in the past']);
      }
      return ok(undefined);
    },
    handle: (data: z.infer<typeof UpdateTaskSchema>, context: any) => {
      const aggregateResult = context.getAggregate();
      if (aggregateResult.isErr()) {
        return err(aggregateResult.error);
      }
      
      const aggregate = aggregateResult.value;
      if (!aggregate.payload || aggregate.payload.aggregateType === 'Empty') {
        return createValidationError('UpdateTask', ['Task not found']);
      }
      
      const task = aggregate.payload as TaskState;
      if (task.status === 'completed') {
        return createValidationError('UpdateTask', ['Cannot update a completed task']);
      }
      
      // Only create event if there are actual changes
      const { taskId, ...updates } = data;
      if (Object.keys(updates).length === 0) {
        return ok([]);
      }
      
      return ok([
        TaskUpdated.create({
          taskId: data.taskId,
          title: data.title,
          description: data.description,
          dueDate: data.dueDate,
          priority: data.priority,
          updatedAt: new Date().toISOString()
        })
      ]);
    }
  }
});

export const DeleteTask = defineCommand({
  type: 'DeleteTask',
  schema: DeleteTaskSchema,
  aggregateType: 'Task',
  projector: TaskProjector,
  handlers: {
    specifyPartitionKeys: (data: z.infer<typeof DeleteTaskSchema>) => PartitionKeys.existing('Task', data.taskId),
    validate: (data: z.infer<typeof DeleteTaskSchema>) => ok(undefined),
    handle: (data: z.infer<typeof DeleteTaskSchema>, context: any) => {
      const aggregateResult = context.getAggregate();
      if (aggregateResult.isErr()) {
        return err(aggregateResult.error);
      }
      
      const aggregate = aggregateResult.value;
      if (!aggregate.payload || aggregate.payload.aggregateType === 'Empty') {
        return createValidationError('DeleteTask', ['Task not found']);
      }
      
      return ok([
        TaskDeleted.create({
          taskId: data.taskId,
          deletedBy: data.deletedBy,
          deletedAt: new Date().toISOString(),
          reason: data.reason
        })
      ]);
    }
  }
});