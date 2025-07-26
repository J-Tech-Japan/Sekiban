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
import { PartitionKeys, CommandValidationError, SekibanError, AggregateNotFoundError } from '@sekiban/core';
import { getExecutor } from '../setup/executor.js';

const router: ExpressRouter = Router();

// Test actor directly
router.get(
  '/test-actor',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      
      const { DaprClient, ActorProxyBuilder, ActorId } = await import('@dapr/dapr');
      const { AggregateActorFactory } = await import('@sekiban/dapr');
      
      const daprClient = new DaprClient({
        daprHost: "127.0.0.1",
        daprPort: "3500"
      });
      
      const AggregateActorClass = AggregateActorFactory.createActorClass();
      const builder = new ActorProxyBuilder(AggregateActorClass, daprClient);
      const actorId = 'test-actor-123';
      const actor = builder.build(new ActorId(actorId));
      
      const testResult = await (actor as any).testMethod();
      
      res.json({ 
        success: true, 
        actorId,
        testResult,
        message: 'Actor test completed' 
      });
    } catch (error) {
      res.status(500).json({ 
        error: error instanceof Error ? error.message : 'Unknown error',
        stack: error instanceof Error ? error.stack : undefined
      });
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
  validateBody((CreateTask as any).schema),
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
        (query as any).take = parseInt(limit as string);
      }
      if (offset) {
        (query as any).skip = parseInt(offset as string);
      }
      
      console.log('[GET ALL] Executing task list query:', {
        queryType: query.constructor.name,
        assignee,
        status,
        limit,
        offset
      });
      
      // Execute multi-projection query
      console.log('[GET ALL] Executing multi-projection query...');
      
      let result;
      try {
        result = await executor.queryAsync(query);
      } catch (error) {
        console.error('[GET ALL] Query execution threw error:', error);
        console.error('[GET ALL] Error stack:', error instanceof Error ? error.stack : 'No stack trace');
        return res.status(500).json({ 
          error: 'Failed to execute query',
          details: error instanceof Error ? error.message : String(error),
          stack: error instanceof Error ? error.stack : undefined
        });
      }
      
      if (result.isErr()) {
        console.error('[GET ALL] Query failed:', result.error);
        console.error('[GET ALL] Error details:', {
          errorType: result.error.constructor.name,
          errorCode: (result.error as any).code,
          errorMessage: result.error.message
        });
        return res.status(500).json({ 
          error: 'Failed to fetch tasks',
          details: result.error.message,
          errorCode: (result.error as any).code
        });
      }
      
      console.log('[GET ALL] Query result:', result.value);
      const tasks = result.value || [];
      
      // Transform results to response format
      const transformedTasks = tasks.map((item: any) => {
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
        data: filteredTasks.slice(query.offset, query.offset + query.limit),
        pagination: {
          total: filteredTasks.length,
          limit: query.limit,
          offset: query.offset
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
      
      // Get the aggregate state directly from the actor (like in create task)
      const { DaprClient, ActorProxyBuilder, ActorId } = await import('@dapr/dapr');
      const { AggregateActorFactory } = await import('@sekiban/dapr');
      
      const daprClient = new DaprClient({
        daprHost: "127.0.0.1",
        daprPort: "3500"
      });
      
      const AggregateActorClass = AggregateActorFactory.createActorClass();
      const builder = new ActorProxyBuilder(AggregateActorClass, daprClient);
      
      // Use the same actor ID pattern as in command execution
      const actorId = `default@Task@${taskId}=TaskProjector`;
      const actor = builder.build(new ActorId(actorId)) as any;
      
      let result: { value: any } | undefined;
      try {
        const aggregateState = await actor.getAggregateStateAsync();
        
        if (!aggregateState) {
          // Try direct approach: load events from EventHandler and project them manually
          const { TaskProjector } = await import('@dapr-sample/domain');
          const projector = new TaskProjector();
          
          // Get events from event handler directly
          const eventHandlerActorId = `Task-${taskId}-default`;
          const eventHandlerActor = builder.build(new ActorId(eventHandlerActorId));
          
          // This won't work with ActorProxyBuilder since it's wrong actor type, let me use direct HTTP call
          const eventsUrl = `http://127.0.0.1:3501/v1.0/actors/AggregateEventHandlerActor/${eventHandlerActorId}/method/getAllEventsAsync`;
          
          const eventsResponse = await fetch(eventsUrl, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({})
          });
          
          const eventsText = await eventsResponse.text();
          
          if (eventsResponse.ok && eventsText && eventsText !== '{}') {
            const events = JSON.parse(eventsText);
            if (events.length > 0) {
              // Project the events manually
              let projectedState: any = null;
              for (const eventDoc of events) {
                // Create a simple projection for TaskCreated
                if (eventDoc.eventType === 'TaskCreated') {
                  projectedState = {
                    aggregateType: 'Task',
                    taskId: eventDoc.payload.taskId,
                    title: eventDoc.payload.title,
                    description: eventDoc.payload.description,
                    priority: eventDoc.payload.priority,
                    status: 'active',
                    createdAt: eventDoc.payload.createdAt,
                    updatedAt: eventDoc.payload.createdAt
                  };
                  break;
                }
              }
              
              if (projectedState) {
                // Handle different possible result structures
                let payload: any = projectedState;
                
                
                const isCompleted = payload.aggregateType === 'CompletedTask' || payload.status === 'completed';
                
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
                return;
              }
            }
          }
          
          const error = new AggregateNotFoundError(taskId, 'Task');
          return next(error);
        }
        
        result = { value: aggregateState };
      } catch (actorError) {
        const error = new AggregateNotFoundError(taskId, 'Task');
        return next(error);
      }

      if (!result.value) {
        const error = new AggregateNotFoundError(taskId, 'Task');
        return next(error);
      }

      // Transform aggregate to response
      
      // Handle different possible result structures
      let payload: any;
      if (result.value && result.value.payload) {
        payload = result.value.payload;
      } else if (result.value && typeof result.value === 'object') {
        // The result might be the payload directly
        payload = result.value;
      } else {
        const error = new AggregateNotFoundError(taskId, 'Task');
        return next(error);
      }
      
      // Check if payload has the expected properties
      if (!payload || typeof payload !== 'object') {
        const error = new AggregateNotFoundError(taskId, 'Task');
        return next(error);
      }
      
      const isCompleted = payload.aggregateType === 'CompletedTask' || payload.status === 'completed';
      
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

// Test loadAggregateAsync directly
router.get(
  '/tasks/:taskId/aggregate-state',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { taskId } = req.params;
      // Create the actor ID in the same format as the executor
      const aggregateId = `default@Task@${taskId}=TaskProjector`;
      
      const { DaprClient, ActorProxyBuilder, ActorId } = await import('@dapr/dapr');
      const { AggregateActorFactory } = await import('@sekiban/dapr');
      
      const daprClient = new DaprClient({
        daprHost: "127.0.0.1",
        daprPort: String(process.env.DAPR_HTTP_PORT || "3500")
      });
      
      const AggregateActorClass = AggregateActorFactory.createActorClass();
      const builder = new ActorProxyBuilder(AggregateActorClass, daprClient);
      const actor = builder.build(new ActorId(aggregateId));
      
      const partitionKeys = {
        aggregateId: taskId,
        group: 'Task',
        rootPartitionKey: 'default'
      };
      const aggregateState = await (actor as any).loadAggregateAsync(partitionKeys);
      
      res.json({ 
        success: true, 
        aggregateId,
        aggregateState,
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