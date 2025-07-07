import express from 'express';
import cors from 'cors';
import { config } from './config/index.js';

const app = express();

// Middleware
app.use(cors());
app.use(express.json());

// Health check
app.get('/health', (req, res) => {
  res.json({
    status: 'healthy',
    timestamp: new Date().toISOString(),
    message: 'Dapr sample is running! Note: Full type checking is temporarily disabled.'
  });
});

// Simple in-memory storage for demo
const tasks = new Map();

// Create task
app.post('/api/tasks', (req, res) => {
  const { title, description, priority, assignedTo } = req.body;
  
  if (!title) {
    return res.status(400).json({ error: 'Title is required' });
  }
  
  const taskId = crypto.randomUUID();
  const task = {
    id: taskId,
    title,
    description,
    priority: priority || 'medium',
    assignedTo,
    status: 'active',
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString()
  };
  
  tasks.set(taskId, task);
  
  res.status(201).json({
    id: taskId,
    message: 'Task created successfully',
    task
  });
});

// Get task by ID
app.get('/api/tasks/:taskId', (req, res) => {
  const { taskId } = req.params;
  const task = tasks.get(taskId);
  
  if (!task) {
    return res.status(404).json({ error: 'Task not found' });
  }
  
  res.json(task);
});

// List all tasks
app.get('/api/tasks', (req, res) => {
  const allTasks = Array.from(tasks.values());
  res.json({
    tasks: allTasks,
    total: allTasks.length
  });
});

// Update task
app.patch('/api/tasks/:taskId', (req, res) => {
  const { taskId } = req.params;
  const task = tasks.get(taskId);
  
  if (!task) {
    return res.status(404).json({ error: 'Task not found' });
  }
  
  const { title, description, priority, status } = req.body;
  
  if (title) task.title = title;
  if (description !== undefined) task.description = description;
  if (priority) task.priority = priority;
  if (status) task.status = status;
  task.updatedAt = new Date().toISOString();
  
  tasks.set(taskId, task);
  
  res.json({
    message: 'Task updated successfully',
    task
  });
});

// Complete task
app.post('/api/tasks/:taskId/complete', (req, res) => {
  const { taskId } = req.params;
  const task = tasks.get(taskId);
  
  if (!task) {
    return res.status(404).json({ error: 'Task not found' });
  }
  
  if (task.status === 'completed') {
    return res.status(400).json({ error: 'Task is already completed' });
  }
  
  task.status = 'completed';
  task.completedAt = new Date().toISOString();
  task.updatedAt = new Date().toISOString();
  
  tasks.set(taskId, task);
  
  res.json({
    message: 'Task completed successfully',
    task
  });
});

// Delete task
app.delete('/api/tasks/:taskId', (req, res) => {
  const { taskId } = req.params;
  
  if (!tasks.has(taskId)) {
    return res.status(404).json({ error: 'Task not found' });
  }
  
  tasks.delete(taskId);
  
  res.json({
    message: 'Task deleted successfully'
  });
});

// Dapr configuration endpoint
app.get('/dapr/config', (req, res) => {
  res.json({
    entities: ['AggregateActor'],
    actorIdleTimeout: '1h',
    drainOngoingCallTimeout: '30s',
    drainRebalancedActors: true
  });
});

// Dapr subscription endpoint
app.get('/dapr/subscribe', (req, res) => {
  res.json([
    {
      pubsubname: 'pubsub',
      topic: 'events',
      route: '/events'
    }
  ]);
});

// Handle events from Dapr pub/sub
app.post('/events', (req, res) => {
  console.log('Received event:', req.body);
  res.status(200).send();
});

// Start server
const PORT = config.PORT || 3000;
app.listen(PORT, () => {
  console.log(`
🚀 Simple Demo Server is running!
📡 Environment: ${config.NODE_ENV}
🔗 URL: http://localhost:${PORT}
🔗 API: http://localhost:${PORT}/api
⚠️  Note: This is a simplified demo without full Sekiban integration

Try these endpoints:
- GET  http://localhost:${PORT}/health
- POST http://localhost:${PORT}/api/tasks
- GET  http://localhost:${PORT}/api/tasks
- GET  http://localhost:${PORT}/api/tasks/{id}
  `);
});