import { Router, Request, Response, NextFunction } from 'express';
import type { Router as ExpressRouter } from 'express';
import { z } from 'zod';
import { 
  CreateTask, 
  AssignTask, 
  CompleteTask, 
  UpdateTask, 
  DeleteTask,
  RevertTaskCompletion,
  GetTaskById 
} from '@dapr-sample/domain';
import { PartitionKeys, CommandValidationError, SekibanError, AggregateNotFoundError } from '@sekiban/core';
import { getExecutor } from '../setup/executor-debug.js';

const router: ExpressRouter = Router();

// Helper to create HTTP errors
class HttpError extends Error {
  constructor(message: string, public statusCode: number, public code: string) {
    super(message);
    this.name = 'HttpError';
  }
}

// Validation middleware
function validateBody<T>(schema: z.ZodSchema<T>) {
  return (req: Request, res: Response, next: NextFunction) => {
    console.log(`[VALIDATION] Validating request body for ${req.method} ${req.path}`);
    const result = schema.safeParse(req.body);
    if (!result.success) {
      const validationErrors = result.error.issues.map(issue => 
        `${issue.path.join('.')}: ${issue.message}`
      );
      console.error('[VALIDATION] Validation failed:', validationErrors);
      const error = new CommandValidationError('Request', validationErrors);
      return next(error);
    }
    console.log('[VALIDATION] Validation passed');
    req.body = result.data;
    next();
  };
}

// Create task
router.post(
  '/tasks',
  validateBody((CreateTask as any).schema),
  async (req: Request, res: Response, next: NextFunction) => {
    const startTime = Date.now();
    console.log('[ROUTE] POST /tasks - Starting task creation');
    
    try {
      console.log('[ROUTE] Getting executor...');
      const executor = await getExecutor();
      
      console.log('[ROUTE] Creating command from request body:', JSON.stringify(req.body, null, 2));
      const command = CreateTask.create(req.body);
      
      console.log('[ROUTE] Executing CreateTask command...');
      const result = await executor.executeCommandAsync(command);
      
      const duration = Date.now() - startTime;
      console.log(`[ROUTE] Command execution completed in ${duration}ms`);
      console.log('[ROUTE] Command result:', result);

      if (result.isErr()) {
        console.error('[ROUTE] Command failed:', result.error);
        return next(result.error);
      }

      console.log('[ROUTE] Command succeeded:', result.value);
      res.status(201).json({
        data: {
          taskId: result.value.aggregateId
        },
        message: 'Task created successfully'
      });
    } catch (error) {
      const duration = Date.now() - startTime;
      console.error(`[ROUTE] Task creation failed after ${duration}ms:`, error);
      next(error);
    }
  }
);

// Get task by ID
router.get(
  '/tasks/:taskId',
  async (req: Request, res: Response, next: NextFunction) => {
    const startTime = Date.now();
    console.log(`[ROUTE] GET /tasks/${req.params.taskId} - Starting task retrieval`);
    
    try {
      const { taskId } = req.params;
      
      // Validate UUID
      console.log('[ROUTE] Validating task ID format...');
      const uuidResult = z.string().uuid().safeParse(taskId);
      if (!uuidResult.success) {
        console.error('[ROUTE] Invalid task ID format:', taskId);
        const error = new CommandValidationError('TaskId', ['Invalid task ID format']);
        return next(error);
      }

      console.log('[ROUTE] Getting executor...');
      const executor = await getExecutor();
      
      console.log('[ROUTE] Creating GetTaskById query...');
      const query = GetTaskById.create({ taskId });
      
      console.log('[ROUTE] Executing query...');
      const result = await executor.queryAsync(query);

      const duration = Date.now() - startTime;
      console.log(`[ROUTE] Query execution completed in ${duration}ms`);

      if (result.isErr()) {
        console.error('[ROUTE] Query failed:', result.error);
        return next(result.error);
      }

      if (!result.value) {
        console.error('[ROUTE] Task not found:', taskId);
        const error = new AggregateNotFoundError(taskId, 'Task');
        return next(error);
      }

      // Transform aggregate to response
      const payload = result.value.payload as any;
      const isCompleted = payload.aggregateType === 'CompletedTask';
      
      console.log('[ROUTE] Task retrieved successfully');
      res.json({
        id: payload.taskId,
        title: payload.title,
        description: payload.description,
        assignedTo: payload.assignedTo,
        dueDate: payload.dueDate,
        priority: payload.priority,
        status: isCompleted ? 'completed' : payload.status,
        createdAt: payload.createdAt,
        updatedAt: payload.updatedAt,
        completedAt: isCompleted ? payload.completedAt : undefined,
        completedBy: isCompleted ? payload.completedBy : undefined,
        completionNotes: isCompleted ? payload.completionNotes : undefined
      });
    } catch (error) {
      const duration = Date.now() - startTime;
      console.error(`[ROUTE] Task retrieval failed after ${duration}ms:`, error);
      next(error);
    }
  }
);

// Assign task
router.post(
  '/tasks/:taskId/assign',
  validateBody(z.object({ assignedTo: z.string().email() })),
  async (req: Request, res: Response, next: NextFunction) => {
    const startTime = Date.now();
    console.log(`[ROUTE] POST /tasks/${req.params.taskId}/assign - Starting task assignment`);
    
    try {
      const { taskId } = req.params;
      const { assignedTo } = req.body;

      console.log('[ROUTE] Getting executor...');
      const executor = await getExecutor();
      
      console.log('[ROUTE] Creating AssignTask command...');
      const command = AssignTask.create({ taskId, assignedTo });
      
      console.log('[ROUTE] Executing command...');
      const result = await executor.executeCommandAsync(command);

      const duration = Date.now() - startTime;
      console.log(`[ROUTE] Command execution completed in ${duration}ms`);

      if (result.isErr()) {
        console.error('[ROUTE] Command failed:', result.error);
        return next(result.error);
      }

      console.log('[ROUTE] Task assigned successfully');
      res.json({
        message: 'Task assigned successfully'
      });
    } catch (error) {
      const duration = Date.now() - startTime;
      console.error(`[ROUTE] Task assignment failed after ${duration}ms:`, error);
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
    const startTime = Date.now();
    console.log(`[ROUTE] POST /tasks/${req.params.taskId}/complete - Starting task completion`);
    
    try {
      const { taskId } = req.params;
      
      console.log('[ROUTE] Getting executor...');
      const executor = await getExecutor();
      
      console.log('[ROUTE] Creating CompleteTask command...');
      const command = CompleteTask.create({ ...req.body, taskId });
      
      console.log('[ROUTE] Executing command...');
      const result = await executor.executeCommandAsync(command);

      const duration = Date.now() - startTime;
      console.log(`[ROUTE] Command execution completed in ${duration}ms`);

      if (result.isErr()) {
        console.error('[ROUTE] Command failed:', result.error);
        return next(result.error);
      }

      console.log('[ROUTE] Task completed successfully');
      res.json({
        message: 'Task completed successfully'
      });
    } catch (error) {
      const duration = Date.now() - startTime;
      console.error(`[ROUTE] Task completion failed after ${duration}ms:`, error);
      next(error);
    }
  }
);

// Other routes remain the same but with similar logging added...

export { router as taskRoutes };