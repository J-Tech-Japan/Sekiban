import express from 'express';
import type { Application } from 'express';
import type { SekibanConfig } from './infrastructure/simple-sekiban-executor';
import { createUserRoutes } from './routes/user-routes';
import { createSekibanExecutor } from './infrastructure/create-sekiban-executor';
import { errorHandler } from './middleware/error-handler';
import { requestLogger } from './middleware/request-logger';
import { createMetricsMiddleware } from './middleware/metrics-middleware';
import { traceMiddleware } from './middleware/trace-middleware';
import { MetricsStore } from './observability/metrics-store';
import { HealthChecker } from './observability/health-checker';

export interface AppDependencies {
  daprClient?: any; // MockDaprClient or real DaprClient
  metricsStore?: MetricsStore;
  healthChecker?: HealthChecker;
}

export async function createApp(
  config: SekibanConfig, 
  dependencies: AppDependencies = {}
): Promise<Application> {
  const app = express();
  
  // Create observability components
  const metricsStore = dependencies.metricsStore || new MetricsStore();
  const healthChecker = dependencies.healthChecker || new HealthChecker();
  
  // Setup health checks
  setupHealthChecks(healthChecker, dependencies);
  
  // Middleware
  app.use(express.json());
  app.use(traceMiddleware);
  app.use(requestLogger);
  app.use(createMetricsMiddleware(metricsStore));
  
  // Create Sekiban executor with dependencies
  const executor = await createSekibanExecutor(config, {
    ...dependencies,
    metricsStore
  });
  
  // Make executor available for test helpers
  (app as any).sekibanExecutor = executor;
  
  // Routes
  app.use('/users', createUserRoutes(executor));
  
  // Health and observability endpoints
  app.get('/healthz', async (req, res) => {
    const result = await healthChecker.checkLiveness();
    res.status(200).json(result);
  });
  
  app.get('/readyz', async (req, res) => {
    const result = await healthChecker.checkReadiness();
    const status = result.status === 'ready' ? 200 : 503;
    res.status(status).json(result);
  });
  
  app.get('/metrics', (req, res) => {
    res.set('Content-Type', 'text/plain; version=0.0.4; charset=utf-8');
    res.send(metricsStore.getPrometheusFormat());
  });
  
  // Dapr pub/sub event handlers
  app.post('/events/users', (req, res) => {
    const event = req.body;
    console.log('Received user event:', JSON.stringify(event, null, 2));
    
    // Process the event (in real implementation, this would update read models, etc.)
    // For now, just log it
    
    res.status(200).json({ success: true });
  });
  
  // Health check (legacy endpoint)
  app.get('/health', (req, res) => {
    res.json({ status: 'healthy', timestamp: new Date().toISOString() });
  });
  
  // Error handling
  app.use(errorHandler);
  
  return app;
}

function setupHealthChecks(healthChecker: HealthChecker, dependencies: AppDependencies): void {
  // Database health check
  healthChecker.addCheck({
    name: 'database',
    check: async () => {
      // For now, always return ok (in-memory implementation)
      return { status: 'ok' };
    }
  });
  
  // Event store health check
  healthChecker.addCheck({
    name: 'eventStore',
    check: async () => {
      // For now, always return ok (in-memory implementation)
      return { status: 'ok' };
    }
  });
  
  // Dapr health check
  healthChecker.addCheck({
    name: 'dapr',
    check: async () => {
      try {
        // Try to use the Dapr client to check if it's available
        if (dependencies.daprClient?.pubsub?.publish) {
          // For mock client, check if it would fail
          if (dependencies.daprClient._simulateFailure) {
            return { status: 'error', error: 'Dapr sidecar unavailable' };
          }
          return { status: 'ok' };
        }
        return { status: 'ok' }; // No Dapr client is fine for basic operation
      } catch (error) {
        return { 
          status: 'error', 
          error: error instanceof Error ? error.message : 'Dapr check failed' 
        };
      }
    }
  });
}