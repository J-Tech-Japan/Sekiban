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
import { getExecutor } from '../setup/executor.js';

const router: ExpressRouter = Router();

// Test actor directly
router.get(
  '/test-actor',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      console.log('[TEST] Testing direct actor invocation...');
      
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
      
      console.log('[TEST] Calling actor testMethod...');
      const testResult = await (actor as any).testMethod();
      console.log('[TEST] Test method result:', testResult);
      
      res.json({ 
        success: true, 
        actorId,
        testResult,
        message: 'Actor test completed' 
      });
    } catch (error) {
      console.error('[TEST] Actor test error:', error);
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
      console.log('Executing command:', JSON.stringify(req.body, null, 2));
      const command = CreateTask.create(req.body);
      const result = await executor.executeCommandAsync(command);
      console.log('Command result:', result);

      if (result.isErr()) {
        console.error('Command failed:', result.error);
        return next(result.error);
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
      
      console.log(`[GET] Getting aggregate state for actor: ${actorId}`);
      
      try {
        const aggregateState = await actor.getAggregateStateAsync();
        console.log('[GET] Raw aggregate state result:', aggregateState);
        console.log('[GET] Aggregate state type:', typeof aggregateState);
        console.log('[GET] Aggregate state keys:', aggregateState ? Object.keys(aggregateState) : 'null');
        
        if (!aggregateState) {
          console.log(`[GET] No aggregate found for task: ${taskId}, trying direct event retrieval`);
          
          // Try direct approach: load events from EventHandler and project them manually
          const { TaskProjector } = await import('@dapr-sample/domain');
          const projector = new TaskProjector();
          
          // Get events from event handler directly
          const eventHandlerActorId = `Task-${taskId}-default`;
          const eventHandlerActor = builder.build(new ActorId(eventHandlerActorId));
          
          console.log(`[GET] Trying to get events directly from EventHandler: ${eventHandlerActorId}`);
          // This won't work with ActorProxyBuilder since it's wrong actor type, let me use direct HTTP call
          const eventsUrl = `http://127.0.0.1:3501/v1.0/actors/AggregateEventHandlerActor/${eventHandlerActorId}/method/getAllEventsAsync`;
          console.log(`[GET] Calling EventHandler directly: ${eventsUrl}`);
          
          const eventsResponse = await fetch(eventsUrl, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({})
          });
          
          console.log(`[GET] Events response status: ${eventsResponse.status}`);
          const eventsText = await eventsResponse.text();
          console.log(`[GET] Events response body: ${eventsText}`);
          
          if (eventsResponse.ok && eventsText && eventsText !== '{}') {
            const events = JSON.parse(eventsText);
            console.log(`[GET] Retrieved ${events.length} events directly`);
            
            if (events.length > 0) {
              // Project the events manually
              let projectedState: any = null;
              for (const eventDoc of events) {
                console.log(`[GET] Projecting event: ${eventDoc.eventType}`);
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
                  console.log(`[GET] Manually projected state: ${JSON.stringify(projectedState, null, 2)}`);
                  break;
                }
              }
              
              if (projectedState) {
                console.log('[GET] Using manually projected state');
                
                // Handle different possible result structures
                let payload: any = projectedState;
                
                console.log('Final payload for response:', JSON.stringify(payload, null, 2));
                
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
        
        console.log('[GET] Aggregate state loaded:', JSON.stringify(aggregateState, null, 2));
        const result = { value: aggregateState };
      } catch (actorError) {
        console.error('[GET] Error calling getAggregateStateAsync:', actorError);
        const error = new AggregateNotFoundError(taskId, 'Task');
        return next(error);
      }

      if (!result.value) {
        const error = new AggregateNotFoundError(taskId, 'Task');
        return next(error);
      }

      // Transform aggregate to response
      console.log('Query result structure:', JSON.stringify(result.value, null, 2));
      
      // Handle different possible result structures
      let payload: any;
      if (result.value && result.value.payload) {
        payload = result.value.payload;
      } else if (result.value && typeof result.value === 'object') {
        // The result might be the payload directly
        payload = result.value;
      } else {
        console.error('Unexpected result structure:', result.value);
        const error = new AggregateNotFoundError(taskId, 'Task');
        return next(error);
      }
      
      console.log('Extracted payload:', JSON.stringify(payload, null, 2));
      
      // Check if payload has the expected properties
      if (!payload || typeof payload !== 'object') {
        console.error('Invalid payload structure:', payload);
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
      
      console.log(`[ASSIGN] Executing AssignTask for taskId: ${taskId}, assignedTo: ${assignedTo}`);
      console.log(`[ASSIGN] Command created:`, JSON.stringify(command, null, 2));
      
      const result = await executor.executeCommandAsync(command);
      
      console.log(`[ASSIGN] Command execution result success: ${result.isOk()}`);
      if (result.isErr()) {
        console.error(`[ASSIGN] Command execution failed:`, result.error);
        console.error(`[ASSIGN] Error details:`, {
          message: result.error.message,
          stack: result.error.stack,
          code: (result.error as any).code
        });
        return next(result.error);
      }
      
      console.log(`[ASSIGN] Command execution successful:`, result.value);

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
      console.log('[TEST] Testing loadAggregateAsync for task:', taskId);
      
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
      
      console.log('[TEST] Calling loadAggregateAsync...');
      const partitionKeys = {
        aggregateId: taskId,
        group: 'Task',
        rootPartitionKey: 'default'
      };
      const aggregateState = await (actor as any).loadAggregateAsync(partitionKeys);
      console.log('[TEST] Aggregate state:', aggregateState);
      
      res.json({ 
        success: true, 
        aggregateId,
        aggregateState,
        message: 'Aggregate state loaded successfully' 
      });
    } catch (error) {
      console.error('[TEST] Load aggregate error:', error);
      res.status(500).json({ 
        error: error instanceof Error ? error.message : 'Unknown error' 
      });
    }
  }
);

export { router as taskRoutes };