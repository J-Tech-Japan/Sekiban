import { Router, Request, Response } from 'express';
import type { Router as ExpressRouter } from 'express';
import { AggregateActor } from '@sekiban/dapr';
import { createTaskDomainTypes } from '@dapr-sample/domain';

const router: ExpressRouter = Router();

// Initialize domain types for actor
const domainTypes = createTaskDomainTypes();

// Dapr actor endpoints
router.get('/dapr/config', (req: Request, res: Response) => {
  res.json({
    entities: ['AggregateActor'],
    actorIdleTimeout: '1h',
    actorScanInterval: '30s',
    drainOngoingCallTimeout: '30s',
    drainRebalancedActors: true
  });
});

// Health check for actors
router.get('/healthz', (req: Request, res: Response) => {
  res.json({ status: 'healthy' });
});

// Actor activation
router.post('/actors/:actorType/:actorId', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId } = req.params;
    console.log(`Activating actor: ${actorType}/${actorId}`);
    
    if (actorType !== 'AggregateActor') {
      return res.status(404).json({ error: 'Unknown actor type' });
    }
    
    // Actor activation is handled by the framework
    res.status(200).send();
  } catch (error) {
    console.error('Error activating actor:', error);
    res.status(500).json({
      error: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// Actor deactivation
router.delete('/actors/:actorType/:actorId', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId } = req.params;
    console.log(`Deactivating actor: ${actorType}/${actorId}`);
    
    if (actorType !== 'AggregateActor') {
      return res.status(404).json({ error: 'Unknown actor type' });
    }
    
    // Actor deactivation is handled by the framework
    res.status(200).send();
  } catch (error) {
    console.error('Error deactivating actor:', error);
    res.status(500).json({
      error: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// Actor method invocation
router.put('/actors/:actorType/:actorId/method/:methodName', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId, methodName } = req.params;
    const payload = req.body;
    
    console.log(`Invoking actor method: ${actorType}/${actorId}/${methodName}`);
    
    if (actorType !== 'AggregateActor') {
      return res.status(404).json({ error: 'Unknown actor type' });
    }
    
    // Create actor instance with domain types
    const actor = new AggregateActor(domainTypes);
    
    // Set actor context (normally done by Dapr runtime)
    (actor as any).actorId = actorId;
    (actor as any).actorType = actorType;
    
    // Invoke the method
    const method = (actor as any)[methodName];
    if (typeof method !== 'function') {
      return res.status(404).json({ error: `Method ${methodName} not found` });
    }
    
    const result = await method.call(actor, payload);
    res.json(result);
  } catch (error) {
    console.error('Error invoking actor method:', error);
    res.status(500).json({
      error: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// Actor reminders
router.put('/actors/:actorType/:actorId/reminders/:reminderName', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId, reminderName } = req.params;
    console.log(`Creating reminder: ${actorType}/${actorId}/${reminderName}`);
    
    // Reminder handling would be implemented here
    res.status(200).send();
  } catch (error) {
    console.error('Error creating reminder:', error);
    res.status(500).json({
      error: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

router.delete('/actors/:actorType/:actorId/reminders/:reminderName', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId, reminderName } = req.params;
    console.log(`Deleting reminder: ${actorType}/${actorId}/${reminderName}`);
    
    // Reminder deletion would be implemented here
    res.status(200).send();
  } catch (error) {
    console.error('Error deleting reminder:', error);
    res.status(500).json({
      error: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// Actor timers
router.put('/actors/:actorType/:actorId/timers/:timerName', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId, timerName } = req.params;
    console.log(`Creating timer: ${actorType}/${actorId}/${timerName}`);
    
    // Timer handling would be implemented here
    res.status(200).send();
  } catch (error) {
    console.error('Error creating timer:', error);
    res.status(500).json({
      error: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

router.delete('/actors/:actorType/:actorId/timers/:timerName', async (req: Request, res: Response) => {
  try {
    const { actorType, actorId, timerName } = req.params;
    console.log(`Deleting timer: ${actorType}/${actorId}/${timerName}`);
    
    // Timer deletion would be implemented here
    res.status(200).send();
  } catch (error) {
    console.error('Error deleting timer:', error);
    res.status(500).json({
      error: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

export { router as actorRoutes };