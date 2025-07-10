import { Router, Request, Response } from 'express';
import type { Router as ExpressRouter } from 'express';
import { config } from '../config/index.js';

const router: ExpressRouter = Router();

// Handle events from Dapr pub/sub
router.post('/events', async (req: Request, res: Response) => {
  try {
    const { data, topic, pubsubname } = req.body;
    
    console.log('Received event:', {
      topic,
      pubsubname,
      eventType: data?.type,
      aggregateId: data?.aggregateId
    });

    // Process event based on type
    // In a real application, you might update read models, trigger workflows, etc.
    
    // Acknowledge the event
    res.status(200).send();
  } catch (error) {
    console.error('Error processing event:', error);
    // Return error to Dapr for retry
    res.status(500).json({
      error: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

export { router as eventRoutes };