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
  GetTaskById,
  TaskListQuery,
  ActiveTaskListQuery,
  TasksByAssigneeQuery
} from '@dapr-sample/domain';
import { CommandValidationError, AggregateNotFoundError } from '@sekiban/core';
import { getExecutor } from '../setup/executor.js';

const router: ExpressRouter = Router();

// Test actor health endpoint
router.get(
  '/test-actor',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const executor = await getExecutor();
      res.json({ 
        success: true, 
        message: 'Executor is available and ready',
        executorType: executor.constructor.name
      });
    } catch (error) {
      next(error);
    }
  }
);

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
    const result = schema.safeParse(req.body);
    if (!result.success) {
      const validationErrors = result.error.issues.map(issue => 
        `${issue.path.join('.')}: ${issue.message}`
      );
      const error = new CommandValidationError('Request', validationErrors);
      return next(error);
    }
    req.body = result.data;
    next();
  };
}

// Create task
router.post(
  '/tasks',
  validateBody(CreateTask.schema),
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const executor = await getExecutor();
      const command = CreateTask.create(req.body);
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        return next(result.error);
      }

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

// Get all tasks
router.get(
  '/tasks',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      // Parse query parameters
      const { status, assignee, limit, offset } = req.query;
      
      const executor = await getExecutor();
      
      // Determine which query to use based on parameters
      let query;
      if (assignee) {
        // Use TasksByAssigneeQuery if assignee is specified
        query = new TasksByAssigneeQuery(assignee as string);
      } else if (status === 'active') {
        // Use ActiveTaskListQuery for active tasks
        query = new ActiveTaskListQuery();
      } else {
        // Use general TaskListQuery
        query = new TaskListQuery();
      }
      
      // Apply pagination
      if (limit) {
        query.take = parseInt(limit as string);
      }
      if (offset) {
        query.skip = parseInt(offset as string);
      }
      
      // Execute multi-projection query
      
      let result;
      try {
        result = await executor.queryAsync(query);
      } catch (error) {
        return res.status(500).json({ 
          error: 'Failed to execute query',
          details: error instanceof Error ? error.message : String(error)
        });
      }
      
      if (result.isErr()) {
        return res.status(500).json({ 
          error: 'Failed to fetch tasks',
          details: result.error.message
        });
      }
      
      const queryResult = result.value || { items: [] };
      const tasks = queryResult.items || [];
      
      // Transform results to response format
      const transformedTasks = tasks.map((item) => {
        const payload = item.payload || item;
        const isCompleted = payload.aggregateType === 'CompletedTask' || payload.status === 'completed';
        
        return {
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
        };
      });
      
      // Apply status filter if needed
      const filteredTasks = status ? 
        transformedTasks.filter(task => task.status === status) : 
        transformedTasks;
      
      res.json({
        data: filteredTasks,
        pagination: {
          total: queryResult.totalCount || filteredTasks.length
        }
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
        const error = new CommandValidationError('TaskId', ['Invalid task ID format']);
        return next(error);
      }

      const executor = await getExecutor();
      
      // Use GetTaskById query to get from AggregateActor
      const query = new GetTaskById({ taskId });
      console.log('[GetTaskById] Executing query for taskId:', taskId);
      const result = await executor.queryAsync(query);
      console.log('[GetTaskById] Query result:', result.isOk() ? 'success' : 'error', result);
      
      if (result.isErr()) {
        console.log('[GetTaskById] Query error:', result.error);
        const error = new AggregateNotFoundError(taskId, 'Task');
        return next(error);
      }
      
      const payload = result.value;
      
      if (!payload || !payload.taskId) {
        const error = new AggregateNotFoundError(taskId, 'Task');
        return next(error);
      }
      
      const isCompleted = payload.aggregateType === 'CompletedTask' || payload.status === 'completed';
      
      res.json({
        data: {
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
        }
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
        return next(result.error);
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
        return next(result.error);
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
        return next(result.error);
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
        return next(result.error);
      }

      res.json({
        message: 'Task deleted successfully'
      });
    } catch (error) {
      next(error);
    }
  }
);

// Revert task completion
router.post(
  '/tasks/:taskId/revert-completion',
  validateBody(z.object({
    revertedBy: z.string().email(),
    reason: z.string().max(500).optional()
  })),
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { taskId } = req.params;
      
      const executor = await getExecutor();
      const command = RevertTaskCompletion.create({ ...req.body, taskId });
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        return next(result.error);
      }

      res.json({
        message: 'Task completion reverted successfully'
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
        return next(result.error);
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

// Test aggregate state through Dapr actor
router.get(
  '/tasks/:taskId/aggregate-state',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { taskId } = req.params;
      
      // Validate UUID
      const uuidResult = z.string().uuid().safeParse(taskId);
      if (!uuidResult.success) {
        return res.status(400).json({ 
          error: 'Invalid task ID format',
          taskId
        });
      }
      
      const executor = await getExecutor();
      
      // Query from multi-projector to get the aggregate state
      const listQuery = new TaskListQuery();
      const listResult = await executor.queryAsync(listQuery);
      
      if (listResult.isErr()) {
        return res.status(500).json({ 
          error: 'Failed to query tasks',
          taskId,
          details: listResult.error.message
        });
      }
      
      const queryResult = listResult.value || { items: [] };
      const tasks = queryResult.items || [];
      
      const task = tasks.find((item: any) => {
        const itemPayload = item.payload || item;
        return itemPayload.taskId === taskId;
      });
      
      if (!task) {
        return res.status(404).json({ 
          error: 'Task not found',
          taskId
        });
      }
      
      res.json({ 
        success: true, 
        taskId,
        aggregateState: task,
        message: 'Aggregate state loaded successfully' 
      });
    } catch (error) {
      res.status(500).json({ 
        error: error instanceof Error ? error.message : 'Unknown error' 
      });
    }
  }
);

export { router as taskRoutes };