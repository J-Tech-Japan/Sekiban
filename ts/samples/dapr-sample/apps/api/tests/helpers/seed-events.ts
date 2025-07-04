import type { Application } from 'express';
import type { IEvent } from '../../src/types/event';

/**
 * Seeds events directly into the executor's event store for testing
 */
export async function seedEvents(app: Application, events: IEvent[]): Promise<void> {
  // Access the executor through the app's locals or a similar mechanism
  // For now, we'll use a direct approach by calling a test endpoint
  
  // Note: This is a test helper - in a real implementation, this would
  // directly access the executor's event store
  const testEndpoint = '/test/seed-events';
  
  // Check if the app has a test endpoint for seeding
  if (app._router && app._router.stack) {
    const hasTestEndpoint = app._router.stack.some((layer: any) => 
      layer.route && layer.route.path === testEndpoint
    );
    
    if (!hasTestEndpoint) {
      // Register the test endpoint dynamically
      app.post(testEndpoint, (req, res) => {
        const { events } = req.body;
        
        // Get the executor from app locals (we'll need to modify the app setup)
        const executor = (app as any).sekibanExecutor;
        
        if (executor && executor.seedEvents) {
          executor.seedEvents(events);
          res.json({ success: true, seededCount: events.length });
        } else {
          res.status(500).json({ error: 'Executor not available for seeding' });
        }
      });
    }
  }
  
  // For now, we'll access the executor directly from the app
  // This is a test-only approach
  const executor = (app as any).sekibanExecutor;
  if (executor && executor.seedEvents) {
    executor.seedEvents(events);
  } else {
    throw new Error('Cannot seed events: executor not available or does not support seeding');
  }
}