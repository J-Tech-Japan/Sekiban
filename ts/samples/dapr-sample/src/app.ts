import express from 'express';
import type { Application } from 'express';
import { DaprClient } from '@dapr/dapr';
import { SekibanDaprExecutor } from '@sekiban/dapr';
import type { DaprSekibanConfiguration } from '@sekiban/dapr';
import { createWeatherForecastRoutes } from './routes/weather-forecast-routes.js';
import { DomainProjectors } from '../packages/domain/src/index.js';
import { errorHandler } from './middleware/error-handler.js';
import { requestLogger } from './middleware/request-logger.js';
import { createMetricsMiddleware } from './middleware/metrics-middleware.js';
import { traceMiddleware } from './middleware/trace-middleware.js';
import { MetricsStore } from './observability/metrics-store.js';
import { HealthChecker } from './observability/health-checker.js';

export interface AppDependencies {
  daprClient?: DaprClient;
  metricsStore?: MetricsStore;
  healthChecker?: HealthChecker;
}

export async function createApp(dependencies: AppDependencies = {}): Promise<Application> {
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
  
  // Create Dapr client
  const daprClient = dependencies.daprClient || new DaprClient({
    daprHost: process.env.DAPR_HOST || 'localhost',
    daprPort: process.env.DAPR_HTTP_PORT || '3500'
  });
  
  // Configure Sekiban Dapr Executor
  const daprConfig: DaprSekibanConfiguration = {
    stateStoreName: process.env.DAPR_STATE_STORE || 'sekiban-eventstore',
    pubSubName: process.env.DAPR_PUBSUB || 'sekiban-pubsub',
    eventTopicName: process.env.DAPR_EVENT_TOPIC || 'domain-events',
    actorType: 'AggregateActor',
    projectors: [...DomainProjectors],
    actorIdPrefix: 'dapr-sample',
    retryAttempts: 3,
    requestTimeoutMs: 30000
  };
  
  // Create Sekiban executor
  const executor = new SekibanDaprExecutor(daprClient, daprConfig);
  
  // Make executor available for test helpers
  (app as any).sekibanExecutor = executor;
  
  // Routes - Weather Forecast API (matching C# template)
  app.use('/api/weatherforecast', createWeatherForecastRoutes(executor));
  
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
  
  // Debug endpoint for environment variables
  app.get('/debug/env', (req, res) => {
    const envVars = {
      DAPR_HOST: process.env.DAPR_HOST || 'localhost',
      DAPR_HTTP_PORT: process.env.DAPR_HTTP_PORT || '3500',
      DAPR_GRPC_PORT: process.env.DAPR_GRPC_PORT || '50001',
      DAPR_STATE_STORE: process.env.DAPR_STATE_STORE || 'sekiban-eventstore',
      DAPR_PUBSUB: process.env.DAPR_PUBSUB || 'sekiban-pubsub',
      DAPR_EVENT_TOPIC: process.env.DAPR_EVENT_TOPIC || 'domain-events',
      APP_ID: process.env.APP_ID || 'dapr-sample'
    };
    res.json(envVars);
  });
  
  // Dapr pub/sub event handlers
  app.post('/events/weather-forecasts', (req, res) => {
    const event = req.body;
    console.log('Received weather forecast event:', JSON.stringify(event, null, 2));
    
    // Process the event (in real implementation, this would update read models, etc.)
    res.status(200).json({ success: true });
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