import express from 'express';
import cors from 'cors';
import { DaprClient } from '@dapr/dapr';
import { z } from 'zod';
import { config } from './config/index.js';

// Import domain schemas directly
// @ts-ignore - Ignoring type errors for demo
import { 
  TaskCreated, 
  TaskAssigned, 
  TaskCompleted,
  CreateTask,
  AssignTask,
  CompleteTask 
} from '@dapr-sample/domain';

const app = express();

// Middleware
app.use(cors());
app.use(express.json());

// Create Dapr client
const daprClient = new DaprClient({
  daprHost: '127.0.0.1',
  daprPort: config.DAPR_HTTP_PORT.toString()
});

// Health check
app.get('/health', (req, res) => {
  res.json({
    status: 'healthy',
    timestamp: new Date().toISOString(),
    message: 'Dapr sample is running with schema-based types!',
    dapr: {
      appId: config.DAPR_APP_ID,
      httpPort: config.DAPR_HTTP_PORT
    }
  });
});

// Create task using schema
app.post('/api/tasks', async (req, res) => {
  try {
    // Validate with schema
    const parseResult = CreateTask.schema.safeParse(req.body);
    if (!parseResult.success) {
      return res.status(400).json({ 
        error: 'Validation failed', 
        details: parseResult.error.format() 
      });
    }

    // Create command
    const command = CreateTask.create(parseResult.data);
    const taskId = crypto.randomUUID();
    
    // Create event
    const event = TaskCreated.create({
      taskId,
      title: command.title,
      description: command.description,
      assignedTo: command.assignedTo,
      dueDate: command.dueDate,
      priority: command.priority,
      createdAt: new Date().toISOString()
    });

    // Store in Dapr state
    await daprClient.state.save(
      config.DAPR_STATE_STORE_NAME,
      [
        {
          key: `task-${taskId}`,
          value: {
            ...event,
            status: 'active',
            version: 1
          }
        }
      ]
    );

    // Publish event
    await daprClient.pubsub.publish(
      config.DAPR_PUBSUB_NAME,
      config.DAPR_EVENT_TOPIC,
      event
    );

    res.status(201).json({
      id: taskId,
      message: 'Task created successfully',
      event
    });
  } catch (error) {
    console.error('Error creating task:', error);
    res.status(500).json({ 
      error: 'Failed to create task',
      details: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// Get task by ID
app.get('/api/tasks/:taskId', async (req, res) => {
  try {
    const { taskId } = req.params;
    
    // Get from Dapr state
    const result = await daprClient.state.get(
      config.DAPR_STATE_STORE_NAME,
      `task-${taskId}`
    );

    if (!result) {
      return res.status(404).json({ error: 'Task not found' });
    }

    res.json(result);
  } catch (error) {
    console.error('Error getting task:', error);
    res.status(500).json({ 
      error: 'Failed to get task',
      details: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// Assign task
app.post('/api/tasks/:taskId/assign', async (req, res) => {
  try {
    const { taskId } = req.params;
    
    // Validate with schema
    const bodySchema = z.object({ assignedTo: z.string().email() });
    const parseResult = bodySchema.safeParse(req.body);
    if (!parseResult.success) {
      return res.status(400).json({ 
        error: 'Validation failed', 
        details: parseResult.error.format() 
      });
    }

    // Get current task
    const task = await daprClient.state.get(
      config.DAPR_STATE_STORE_NAME,
      `task-${taskId}`
    );

    if (!task) {
      return res.status(404).json({ error: 'Task not found' });
    }

    // Create event
    const event = TaskAssigned.create({
      taskId,
      assignedTo: parseResult.data.assignedTo,
      assignedAt: new Date().toISOString()
    });

    // Update state
    const taskData = typeof task === 'string' ? JSON.parse(task) : task;
    const updatedTask = {
      ...taskData,
      assignedTo: parseResult.data.assignedTo,
      updatedAt: new Date().toISOString(),
      version: ((taskData as any).version || 1) + 1
    };

    await daprClient.state.save(
      config.DAPR_STATE_STORE_NAME,
      [{ key: `task-${taskId}`, value: updatedTask }]
    );

    // Publish event
    await daprClient.pubsub.publish(
      config.DAPR_PUBSUB_NAME,
      config.DAPR_EVENT_TOPIC,
      event
    );

    res.json({
      message: 'Task assigned successfully',
      event
    });
  } catch (error) {
    console.error('Error assigning task:', error);
    res.status(500).json({ 
      error: 'Failed to assign task',
      details: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// Complete task
app.post('/api/tasks/:taskId/complete', async (req, res) => {
  try {
    const { taskId } = req.params;
    
    // Validate with schema
    const bodySchema = z.object({
      completedBy: z.string().email(),
      notes: z.string().max(500).optional()
    });
    const parseResult = bodySchema.safeParse(req.body);
    if (!parseResult.success) {
      return res.status(400).json({ 
        error: 'Validation failed', 
        details: parseResult.error.format() 
      });
    }

    // Get current task
    const task = await daprClient.state.get(
      config.DAPR_STATE_STORE_NAME,
      `task-${taskId}`
    );

    if (!task) {
      return res.status(404).json({ error: 'Task not found' });
    }

    const taskData = typeof task === 'string' ? JSON.parse(task) : task;
    
    if ((taskData as any).status === 'completed') {
      return res.status(400).json({ error: 'Task is already completed' });
    }

    // Create event
    const event = TaskCompleted.create({
      taskId,
      completedBy: parseResult.data.completedBy,
      completedAt: new Date().toISOString(),
      notes: parseResult.data.notes
    });

    // Update state
    const updatedTask = {
      ...taskData,
      status: 'completed',
      completedAt: event.completedAt,
      updatedAt: new Date().toISOString(),
      version: ((taskData as any).version || 1) + 1
    };

    await daprClient.state.save(
      config.DAPR_STATE_STORE_NAME,
      [{ key: `task-${taskId}`, value: updatedTask }]
    );

    // Publish event
    await daprClient.pubsub.publish(
      config.DAPR_PUBSUB_NAME,
      config.DAPR_EVENT_TOPIC,
      event
    );

    res.json({
      message: 'Task completed successfully',
      event
    });
  } catch (error) {
    console.error('Error completing task:', error);
    res.status(500).json({ 
      error: 'Failed to complete task',
      details: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// List all tasks
app.get('/api/tasks', async (req, res) => {
  try {
    // In a real app, this would query the event store or a read model
    // For demo, we'll return a simple response
    res.json({
      message: 'Task listing would query the event store',
      tasks: [],
      total: 0
    });
  } catch (error) {
    console.error('Error listing tasks:', error);
    res.status(500).json({ 
      error: 'Failed to list tasks',
      details: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// Dapr configuration endpoint
app.get('/dapr/config', (req, res) => {
  res.json({
    entities: [config.DAPR_ACTOR_TYPE],
    actorIdleTimeout: '1h',
    drainOngoingCallTimeout: '30s',
    drainRebalancedActors: true
  });
});

// Dapr subscription endpoint
app.get('/dapr/subscribe', (req, res) => {
  res.json([
    {
      pubsubname: config.DAPR_PUBSUB_NAME,
      topic: config.DAPR_EVENT_TOPIC,
      route: '/events'
    }
  ]);
});

// Handle events from Dapr pub/sub
app.post('/events', (req, res) => {
  console.log('Received event:', {
    topic: req.body.topic,
    data: req.body.data
  });
  res.status(200).send();
});

// Start server
const PORT = config.PORT || 3000;
app.listen(PORT, () => {
  console.log(`
🚀 Demo Server with Schema-Based Types is running!
📡 Environment: ${config.NODE_ENV}
🔗 URL: http://localhost:${PORT}
🔗 API: http://localhost:${PORT}/api
🎭 Dapr App ID: ${config.DAPR_APP_ID}
📦 Using Dapr State Store: ${config.DAPR_STATE_STORE_NAME}
📨 Using Dapr PubSub: ${config.DAPR_PUBSUB_NAME}

This demo shows:
- Schema-based event/command definitions with Zod
- Dapr state management
- Event publishing via Dapr pub/sub
- Runtime validation using schemas

Try these endpoints:
- GET  http://localhost:${PORT}/health
- POST http://localhost:${PORT}/api/tasks
- GET  http://localhost:${PORT}/api/tasks/{id}
- POST http://localhost:${PORT}/api/tasks/{id}/assign
- POST http://localhost:${PORT}/api/tasks/{id}/complete
  `);
});