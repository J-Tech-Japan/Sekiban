import { Router } from 'express';
import { DaprClient, ActorId } from '@dapr/dapr';
import { config } from '../config/index.js';
import logger from '../utils/logger.js';

export const multiProjectorRoutes = Router();

// Get a multi-projection state
multiProjectorRoutes.get('/multi-projections/:projectorType/:id', async (req, res, next) => {
  try {
    const { projectorType, id } = req.params;
    
    logger.info(`Getting multi-projection state for ${projectorType}/${id}`);
    
    const daprClient = new DaprClient({
      daprHost: "127.0.0.1",
      daprPort: String(config.DAPR_HTTP_PORT)
    });
    
    const actorId = new ActorId(`${projectorType}:${id}`);
    
    // Call the MultiProjectorActor's getProjectionAsync method
    const response = await daprClient.actor.invoke(
      'MultiProjectorActor',
      actorId.id,
      'getProjectionAsync'
    );
    
    res.json({
      projectorType,
      id,
      state: response
    });
  } catch (error) {
    logger.error('Error getting multi-projection:', error);
    next(error);
  }
});

// Query multi-projections
multiProjectorRoutes.post('/multi-projections/query', async (req, res, next) => {
  try {
    const { projectorType, query } = req.body;
    
    logger.info(`Querying multi-projections for ${projectorType}`);
    
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
    logger.error('Error querying multi-projections:', error);
    next(error);
  }
});