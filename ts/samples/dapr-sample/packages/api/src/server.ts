import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import compression from 'compression';
import morgan from 'morgan';
import { config } from './config/index.js';
import { createExecutor } from './setup/executor.js';
import { errorHandler } from './middleware/error-handler.js';
import { healthRoutes } from './routes/health-routes.js';
import { taskRoutes } from './routes/task-routes.js';
import { eventRoutes } from './routes/event-routes.js';
// Dapr actor server is provided by the Sekiban Dapr package

async function startServer() {
  const app = express();

  // Middleware
  app.use(helmet());
  app.use(cors({ origin: config.CORS_ORIGIN }));
  app.use(compression());
  app.use(morgan(config.NODE_ENV === 'production' ? 'combined' : 'dev'));
  app.use(express.json());
  app.use(express.urlencoded({ extended: true }));

  // Routes
  app.use('/', healthRoutes);
  app.use('/', eventRoutes);
  // app.use('/', actorRoutes); // Removed: Not needed for Sekiban
  app.use(config.API_PREFIX, taskRoutes);

  // Error handling
  app.use(errorHandler);

  // Note: The Dapr actor server is managed by the Sekiban Dapr package
  // and runs as a separate process invoked by Dapr sidecar

  // Initialize executor
  try {
    await createExecutor();
    console.log('Executor initialized successfully');
  } catch (error) {
    console.error('Failed to initialize executor:', error);
    process.exit(1);
  }

  // Start server
  const server = app.listen(config.PORT, () => {
    console.log(`
🚀 Server is running!
📡 Environment: ${config.NODE_ENV}
🔗 URL: http://localhost:${config.PORT}
🔗 API: http://localhost:${config.PORT}${config.API_PREFIX}
🎭 Dapr App ID: ${config.DAPR_APP_ID}
    `);
  });

  // Graceful shutdown
  const gracefulShutdown = async (signal: string) => {
    console.log(`\n${signal} received, starting graceful shutdown...`);
    
    server.close(() => {
      console.log('HTTP server closed');
      process.exit(0);
    });

    // Force exit after 30 seconds
    setTimeout(() => {
      console.error('Forced shutdown after timeout');
      process.exit(1);
    }, 30000);
  };

  process.on('SIGTERM', () => gracefulShutdown('SIGTERM'));
  process.on('SIGINT', () => gracefulShutdown('SIGINT'));
}

// Start the server
startServer().catch((error) => {
  console.error('Failed to start server:', error);
  process.exit(1);
});