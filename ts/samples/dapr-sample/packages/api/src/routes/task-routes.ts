import { Router, Request, Response, NextFunction } from 'express';
import type { Router as ExpressRouter } from 'express';
import { z } from 'zod';
import { 
  CreateTask, 
  AssignTask, 
  CompleteTask, 
  UpdateTask, 
  DeleteTask,
  GetTaskById 
} from '@dapr-sample/domain';
import { PartitionKeys } from '@sekiban/core';
import { getExecutor } from '../setup/executor.js';
import { ApiError } from '../middleware/error-handler.js';

const router: ExpressRouter = Router();

// Validation middleware
function validateBody<T>(schema: z.ZodSchema<T>) {
  return (req: Request, res: Response, next: NextFunction) => {
    const result = schema.safeParse(req.body);
    if (!result.success) {
      const error: ApiError = new Error('Validation failed');
      error.statusCode = 400;
      error.code = 'VALIDATION_ERROR';
      error.details = result.error.format();
      return next(error);
    }
    req.body = result.data;
    next();
  };
}

// Create task
router.post(
  '/tasks',
  validateBody((CreateTask as any).schema),
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const executor = await getExecutor();
      console.log('Executing command:', JSON.stringify(req.body, null, 2));
      const command = CreateTask.create(req.body);
      const result = await executor.executeCommandAsync(command);
      console.log('Command result:', result);

      if (result.isErr()) {
        console.error('Command failed:', result.error);
        const error: ApiError = new Error(result.error.message || 'Failed to create task');
        error.statusCode = 400;
        error.code = result.error.code || 'UNKNOWN_ERROR';
        return next(error);
      }

      console.log('Command succeeded:', result.value);
      res.status(201).json({
        data: {
          taskId: result.value.aggregateId
        },
        message: 'Task created successfully'
      });
    } catch (error) {
      next(error);
    }
  }
);

// Get task by ID
router.get(
  '/tasks/:taskId',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { taskId } = req.params;
      
      // Validate UUID
      const uuidResult = z.string().uuid().safeParse(taskId);
      if (!uuidResult.success) {
        const error: ApiError = new Error('Invalid task ID format');
        error.statusCode = 400;
        error.code = 'INVALID_ID';
        return next(error);
      }

      const executor = await getExecutor();
      const query = GetTaskById.create({ taskId });
      const result = await executor.queryAsync(query);

      if (result.isErr()) {
        const error: ApiError = new Error(result.error.message || 'Failed to get task');
        error.statusCode = 500;
        error.code = result.error.code || 'UNKNOWN_ERROR';
        return next(error);
      }

      if (!result.value || result.value.payload.aggregateType !== 'Task') {
        const error: ApiError = new Error('Task not found');
        error.statusCode = 404;
        error.code = 'NOT_FOUND';
        return next(error);
      }

      // Transform aggregate to response
      const task = result.value.payload as any;
      res.json({
        id: task.taskId,
        title: task.title,
        description: task.description,
        assignedTo: task.assignedTo,
        dueDate: task.dueDate,
        priority: task.priority,
        status: task.status,
        createdAt: task.createdAt,
        updatedAt: task.updatedAt,
        completedAt: task.completedAt
      });
    } catch (error) {
      next(error);
    }
  }
);

// Assign task
router.post(
  '/tasks/:taskId/assign',
  validateBody(z.object({ assignedTo: z.string().email() })),
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { taskId } = req.params;
      const { assignedTo } = req.body;

      const executor = await getExecutor();
      const command = AssignTask.create({ taskId, assignedTo });
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        const error: ApiError = new Error(result.error.message || 'Failed to assign task');
        error.statusCode = 400;
        error.code = result.error.code || 'UNKNOWN_ERROR';
        return next(error);
      }

      res.json({
        message: 'Task assigned successfully'
      });
    } catch (error) {
      next(error);
    }
  }
);

// Complete task
router.post(
  '/tasks/:taskId/complete',
  validateBody(z.object({
    completedBy: z.string().email(),
    notes: z.string().max(500).optional()
  })),
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { taskId } = req.params;
      
      const executor = await getExecutor();
      const command = CompleteTask.create({ ...req.body, taskId });
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        const error: ApiError = new Error(result.error.message || 'Failed to complete task');
        error.statusCode = 400;
        error.code = result.error.code || 'UNKNOWN_ERROR';
        return next(error);
      }

      res.json({
        message: 'Task completed successfully'
      });
    } catch (error) {
      next(error);
    }
  }
);

// Update task
router.patch(
  '/tasks/:taskId',
  validateBody(z.object({
    title: z.string().min(1).max(200).optional(),
    description: z.string().max(1000).optional(),
    dueDate: z.string().datetime().optional(),
    priority: z.enum(['low', 'medium', 'high']).optional()
  })),
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { taskId } = req.params;
      
      const executor = await getExecutor();
      const command = UpdateTask.create({ ...req.body, taskId });
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        const error: ApiError = new Error(result.error.message || 'Failed to update task');
        error.statusCode = 400;
        error.code = result.error.code || 'UNKNOWN_ERROR';
        return next(error);
      }

      res.json({
        message: 'Task updated successfully'
      });
    } catch (error) {
      next(error);
    }
  }
);

// Delete task
router.delete(
  '/tasks/:taskId',
  validateBody(z.object({
    deletedBy: z.string().email(),
    reason: z.string().max(200).optional()
  })),
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { taskId } = req.params;
      
      const executor = await getExecutor();
      const command = DeleteTask.create({ ...req.body, taskId });
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        const error: ApiError = new Error(result.error.message || 'Failed to delete task');
        error.statusCode = 400;
        error.code = result.error.code || 'UNKNOWN_ERROR';
        return next(error);
      }

      res.json({
        message: 'Task deleted successfully'
      });
    } catch (error) {
      next(error);
    }
  }
);

// Workflow: Create and assign task
router.post(
  '/workflows/task-assignment',
  validateBody(z.object({
    title: z.string(),
    description: z.string().optional(),
    assignedTo: z.string().email(),
    priority: z.enum(['low', 'medium', 'high']).default('medium'),
    dueDate: z.string().datetime().optional()
  })),
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const executor = await getExecutor();
      
      // Create task with assignedTo already set
      const command = CreateTask.create({
        title: req.body.title,
        description: req.body.description,
        assignedTo: req.body.assignedTo,
        priority: req.body.priority,
        dueDate: req.body.dueDate
      });
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        const error: ApiError = new Error(result.error.message || 'Workflow failed');
        error.statusCode = 400;
        error.code = result.error.code || 'UNKNOWN_ERROR';
        return next(error);
      }

      res.status(201).json({
        taskId: result.value.aggregateId,
        message: 'Task created and assigned successfully'
      });
    } catch (error) {
      next(error);
    }
  }
);

export { router as taskRoutes };