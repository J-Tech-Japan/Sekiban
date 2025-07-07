import { Router, Request, Response } from 'express';
import type { Router as ExpressRouter } from 'express';
import { config } from '../config/index.js';
import { createActor } from '../actors/aggregate-actor.js';

const router: ExpressRouter = Router();

// Actor instances cache
const actorInstances = new Map<string, any>();

async function getActorInstance(actorId: string): Promise<any> {
  if (!actorInstances.has(actorId)) {
    const actor = await createActor(actorId);
    actorInstances.set(actorId, actor);
  }
  return actorInstances.get(actorId);
}

// Create a simple actor-like handler 
async function handleActorMethod(actorId: string, methodName: string, methodData: any) {
  
  switch (methodName) {
    case 'executeCommandAsync': {
      // Get actor instance
      const actor = await getActorInstance(actorId);
      
      console.log('Actor executeCommandAsync called with:', {
        actorId,
        methodData: JSON.stringify(methodData, null, 2)
      });
      
      // Delegate to actor
      return await actor.executeCommandAsync(methodData);
    }
    
    case 'getAggregateStateAsync': {
      // Get actor instance
      const actor = await getActorInstance(actorId);
      return await actor.getAggregateStateAsync(methodData);
    }
    
    case 'queryAsync': {
      // Get actor instance
      const actor = await getActorInstance(actorId);
      
      console.log('Actor queryAsync called with:', {
        actorId,
        methodData
      });
      
      return await actor.queryAsync(methodData);
    }
    
    case 'getAllEventsAsync': {
      // Get actor instance
      const actor = await getActorInstance(actorId);
      return await actor.getAllEventsAsync();
    }
    
    case 'getDeltaEventsAsync': {
      // Get actor instance
      const actor = await getActorInstance(actorId);
      const { fromSortableUniqueId, limit } = methodData;
      return await actor.getDeltaEventsAsync(fromSortableUniqueId, limit);
    }
    
    case 'getLastSortableUniqueIdAsync': {
      // Get actor instance
      const actor = await getActorInstance(actorId);
      return await actor.getLastSortableUniqueIdAsync();
    }
    
    default:
      throw new Error(`Unknown method: ${methodName}`);
  }
}

// Actor method invocation
router.post('/actors/:actorType/:actorId/method/:methodName', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId, methodName } = req.params;
    const methodData = req.body;

    console.log(`Actor method call: ${actorType}/${actorId}/${methodName}`);
    console.log('Method data:', JSON.stringify(methodData, null, 2));

    if (actorType !== config.DAPR_ACTOR_TYPE) {
      return res.status(404).json({ error: `Unknown actor type: ${actorType}` });
    }

    const result = await handleActorMethod(actorId, methodName, methodData);
    return res.json(result);
    
  } catch (error) {
    console.error('Actor method invocation error:', error);
    res.status(500).json({ 
      error: 'Internal server error', 
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// Actor state management
router.get('/actors/:actorType/:actorId/state/:key', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId, key } = req.params;
    
    // For now, return empty state
    res.json({});
  } catch (error) {
    console.error('Actor state get error:', error);
    res.status(500).json({ 
      error: 'Internal server error', 
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

router.post('/actors/:actorType/:actorId/state', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId } = req.params;
    const stateData = req.body;
    
    // For now, just acknowledge
    
    res.status(204).send();
  } catch (error) {
    console.error('Actor state set error:', error);
    res.status(500).json({ 
      error: 'Internal server error', 
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

router.delete('/actors/:actorType/:actorId/state/:key', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId, key } = req.params;
    
    // For now, just acknowledge
    
    res.status(204).send();
  } catch (error) {
    console.error('Actor state delete error:', error);
    res.status(500).json({ 
      error: 'Internal server error', 
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// Actor reminders
router.post('/actors/:actorType/:actorId/reminders/:reminderName', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId, reminderName } = req.params;
    const reminderData = req.body;
    
    // For now, just acknowledge
    
    res.status(204).send();
  } catch (error) {
    console.error('Actor reminder error:', error);
    res.status(500).json({ 
      error: 'Internal server error', 
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

router.delete('/actors/:actorType/:actorId/reminders/:reminderName', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId, reminderName } = req.params;
    
    // Actor reminder deletion would be handled by Dapr, this is just the callback
    
    res.status(204).send();
  } catch (error) {
    console.error('Actor reminder deletion error:', error);
    res.status(500).json({ 
      error: 'Internal server error', 
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// Actor timers
router.post('/actors/:actorType/:actorId/timers/:timerName', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId, timerName } = req.params;
    const timerData = req.body;
    
    // For now, just acknowledge timer callback
    
    res.status(204).send();
  } catch (error) {
    console.error('Actor timer error:', error);
    res.status(500).json({ 
      error: 'Internal server error', 
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

router.delete('/actors/:actorType/:actorId/timers/:timerName', async (req: Request, res: Response) => {
  try {
    // Timer deletion callback
    res.status(204).send();
  } catch (error) {
    console.error('Actor timer deletion error:', error);
    res.status(500).json({ 
      error: 'Internal server error', 
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

export { router as actorRoutes };