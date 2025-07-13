import { AbstractActor, ActorId, DaprClient, DaprServer, ActorProxyBuilder } from '@dapr/dapr';

// Interface for WorkerActor
interface IWorkerActor {
  doWork(message: string): Promise<string>;
}

// Interface for CoordinatorActor
interface ICoordinatorActor {
  coordinateWork(task: string): Promise<string>;
}

/**
 * WorkerActor - receives work requests and processes them
 */
class WorkerActor extends AbstractActor implements IWorkerActor {
  static get actorType() { 
    return "WorkerActor"; 
  }

  constructor(daprClient: DaprClient, id: ActorId) {
    super(daprClient, id);
    console.log('[WorkerActor] Created with ID:', id.getId());
  }

  async onActivate(): Promise<void> {
    console.log('[WorkerActor] Activated');
  }

  async doWork(message: string): Promise<string> {
    console.log('[WorkerActor] doWork called with:', message);
    const result = `Worker ${this.getActorId().getId()} processed: ${message}`;
    console.log('[WorkerActor] Returning:', result);
    return result;
  }
}

/**
 * CoordinatorActor - coordinates work by calling WorkerActors
 */
class CoordinatorActor extends AbstractActor implements ICoordinatorActor {
  static get actorType() { 
    return "CoordinatorActor"; 
  }

  constructor(daprClient: DaprClient, id: ActorId) {
    super(daprClient, id);
    console.log('[CoordinatorActor] Created with ID:', id.getId());
  }

  async onActivate(): Promise<void> {
    console.log('[CoordinatorActor] Activated');
  }

  async coordinateWork(task: string): Promise<string> {
    console.log('[CoordinatorActor] coordinateWork called with:', task);

    try {
      // Use DaprClient to invoke actor method directly
      const workerId = `worker-${Date.now()}`;
      console.log('[CoordinatorActor] Calling WorkerActor with ID:', workerId);
      
      // Option 1: Use ActorProxyBuilder with the actor runtime's DaprClient
      const daprClient = this.getDaprClient();
      const builder = new ActorProxyBuilder<IWorkerActor>(daprClient);
      const workerProxy = builder.build(new ActorId(workerId), "WorkerActor");
      
      console.log('[CoordinatorActor] Calling WorkerActor.doWork()...');
      const result = await workerProxy.doWork(task);
      
      console.log('[CoordinatorActor] Received response from WorkerActor:', result);
      return `Coordinator ${this.getActorId().getId()} got: ${result}`;
    } catch (error) {
      console.error('[CoordinatorActor] Error calling WorkerActor:', error);
      
      // Fallback: Try direct invocation via DaprClient
      try {
        console.log('[CoordinatorActor] Trying direct invocation...');
        const workerId = `worker-fallback-${Date.now()}`;
        const daprClient = this.getDaprClient();
        
        // Invoke actor method directly
        const result = await daprClient.actor.invoke(
          "WorkerActor",
          workerId,
          "doWork",
          task
        );
        
        console.log('[CoordinatorActor] Direct invocation succeeded:', result);
        return `Coordinator ${this.getActorId().getId()} got (via direct): ${result}`;
      } catch (fallbackError) {
        console.error('[CoordinatorActor] Direct invocation also failed:', fallbackError);
        throw error;
      }
    }
  }
}

/**
 * Main function to set up and start the Dapr server
 */
async function main() {
  console.log('=== Starting Actor Communication Test ===\n');

  const daprHost = process.env.DAPR_HOST || "127.0.0.1";
  const daprPort = process.env.DAPR_HTTP_PORT || "3500";
  const serverHost = process.env.SERVER_HOST || "127.0.0.1";
  const serverPort = process.env.SERVER_PORT || "3004";

  console.log('Configuration:');
  console.log(`  Dapr Host: ${daprHost}:${daprPort}`);
  console.log(`  Server: ${serverHost}:${serverPort}`);

  // Create DaprServer
  const server = new DaprServer({
    serverHost,
    serverPort,
    clientOptions: {
      daprHost,
      daprPort
    }
  });

  // Register both actors
  console.log('\nRegistering actors...');
  await server.actor.registerActor(WorkerActor);
  console.log('  ✓ Registered WorkerActor');
  
  await server.actor.registerActor(CoordinatorActor);
  console.log('  ✓ Registered CoordinatorActor');

  // Initialize actor runtime
  console.log('\nInitializing actor runtime...');
  await server.actor.init();
  console.log('  ✓ Actor runtime initialized');

  // Start the server
  console.log('\nStarting server...');
  await server.start();
  console.log(`  ✓ Server started on port ${serverPort}`);

  console.log('\n=== Server Ready ===');
  console.log(`Check registered actors: http://localhost:${serverPort}/dapr/config`);
  console.log('\nTest the actors with:');
  console.log(`  curl -X PUT http://localhost:${daprPort}/v1.0/actors/CoordinatorActor/test-coordinator/method/coordinateWork \\`);
  console.log(`    -H "Content-Type: application/json" \\`);
  console.log(`    -d '"Hello from test"'`);

  // Handle graceful shutdown
  process.on('SIGINT', async () => {
    console.log('\n\nShutting down...');
    await server.stop();
    process.exit(0);
  });

  process.on('SIGTERM', async () => {
    console.log('\n\nShutting down...');
    await server.stop();
    process.exit(0);
  });
}

// Start the application
main().catch(error => {
  console.error('Failed to start:', error);
  process.exit(1);
});