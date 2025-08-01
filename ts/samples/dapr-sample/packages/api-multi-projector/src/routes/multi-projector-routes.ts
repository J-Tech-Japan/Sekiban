import { Router } from 'express';
import type { Router as ExpressRouter } from 'express';
import { DaprClient, ActorId, HttpMethod } from '@dapr/dapr';
import { config } from '../config/index.js';
import logger from '../utils/logger.js';

export const multiProjectorRoutes: ExpressRouter = Router();

// Get a multi-projection state
multiProjectorRoutes.get('/multi-projections/:projectorType/:id', async (req, res, next) => {
  try {
    const { projectorType, id } = req.params;
    
    
    const daprClient = new DaprClient({
      daprHost: "127.0.0.1",
      daprPort: String(config.DAPR_HTTP_PORT)
    });
    
    const actorId = new ActorId(`${projectorType}:${id}`);
    
    // Call the MultiProjectorActor's getProjectionAsync method using invoker
    const response = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector', // app-id
      `actors/MultiProjectorActor/${actorId.getId()}/method/getProjectionAsync`,
      HttpMethod.PUT,
      {} // empty body for GET-like operation
    );
    
    res.json({
      projectorType,
      id,
      state: response
    });
  } catch (error) {
    next(error);
  }
});

// Query multi-projections
multiProjectorRoutes.post('/multi-projections/query', async (req, res, next) => {
  try {
    const { projectorType, query } = req.body;
    
    
    const daprClient = new DaprClient({
      daprHost: "127.0.0.1",
      daprPort: String(config.DAPR_HTTP_PORT)
    });
    
    // For queries, we might need a different approach
    // This is a placeholder for now
    res.json({
      message: 'Multi-projection queries not yet implemented',
      projectorType,
      query
    });
  } catch (error) {
    next(error);
  }
});