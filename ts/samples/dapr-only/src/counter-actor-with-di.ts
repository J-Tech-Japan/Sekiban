import { AbstractActor, ActorId, DaprClient } from '@dapr/dapr';
import { getCradle, type Logger, type CounterService, type AppConfig } from './di-container.js';

/**
 * Implementation class that contains the business logic
 * This is separated from the actor to allow for easier testing and DI
 */
class CounterActorImpl {
  private readonly STATE_KEY = 'count';
  
  constructor(
    private readonly actorId: string,
    private readonly logger: Logger,
    private readonly counterService: CounterService,
    private readonly config: AppConfig,
    private readonly stateManager: any // Would be IActorStateManager in real implementation
  ) {
    this.logger.log(`CounterActorImpl created for actor ${actorId}`);
  }

  async increment(): Promise<number> {
    this.logger.log(`increment() called for actor ${this.actorId}`);
    
    const [hasState, currentCount] = await this.stateManager.tryGetState(this.STATE_KEY) as [boolean, number | undefined];
    const count = hasState ? currentCount! : this.config.defaultValue;
    
    const newCount = this.counterService.calculateNewValue(count, 'increment');
    
    if (!this.counterService.validateCount(newCount)) {
      throw new Error(`Count ${newCount} exceeds maximum allowed value of ${this.config.maxCount}`);
    }
    
    await this.stateManager.setState(this.STATE_KEY, newCount);
    this.logger.log(`Count incremented from ${count} to ${newCount}`);
    
    return newCount;
  }

  async decrement(): Promise<number> {
    this.logger.log(`decrement() called for actor ${this.actorId}`);
    
    const [hasState, currentCount] = await this.stateManager.tryGetState(this.STATE_KEY) as [boolean, number | undefined];
    const count = hasState ? currentCount! : this.config.defaultValue;
    
    const newCount = this.counterService.calculateNewValue(count, 'decrement');
    
    if (!this.counterService.validateCount(newCount)) {
      throw new Error(`Count ${newCount} is below minimum allowed value of ${this.config.minCount}`);
    }
    
    await this.stateManager.setState(this.STATE_KEY, newCount);
    this.logger.log(`Count decremented from ${count} to ${newCount}`);
    
    return newCount;
  }

  async getCount(): Promise<number> {
    this.logger.log(`getCount() called for actor ${this.actorId}`);
    
    const [hasState, currentCount] = await this.stateManager.tryGetState(this.STATE_KEY) as [boolean, number | undefined];
    const count = hasState ? currentCount! : this.config.defaultValue;
    
    this.logger.log(`Current count is ${count}`);
    return count;
  }

  async reset(): Promise<void> {
    this.logger.log(`reset() called for actor ${this.actorId}`);
    
    await this.stateManager.setState(this.STATE_KEY, this.config.defaultValue);
    this.logger.log(`Count reset to ${this.config.defaultValue}`);
  }
}

/**
 * Counter actor that uses Awilix DI
 * This is the wrapper that satisfies Dapr's actor requirements
 */
export class CounterActorWithDI extends AbstractActor {
  private impl: CounterActorImpl | null = null;
  private actorIdString: string;

  constructor(daprClient: DaprClient, id: ActorId) {
    super(daprClient, id);
    
    // Extract actor ID as string
    this.actorIdString = (id as any).id || String(id);
    console.log(`[CounterActorWithDI] Constructor called for ${this.actorIdString}`);
    
    // Note: We can't initialize the implementation here because we need the state manager
    // which is only available after the actor is activated
  }

  async onActivate(): Promise<void> {
    console.log(`[CounterActorWithDI] onActivate called for ${this.actorIdString}`);
    
    // Get dependencies from Awilix container
    const { logger, counterService, config } = getCradle();
    
    // Get the state manager
    const stateManager = await this.getStateManager();
    
    // Create the implementation with injected dependencies
    this.impl = new CounterActorImpl(
      this.actorIdString,
      logger,
      counterService,
      config,
      stateManager
    );
    
    logger.log(`Actor ${this.actorIdString} activated with DI`);
  }

  async onDeactivate(): Promise<void> {
    console.log(`[CounterActorWithDI] onDeactivate called for ${this.actorIdString}`);
    const { logger } = getCradle();
    logger.log(`Actor ${this.actorIdString} deactivated`);
  }

  // Delegate all methods to the implementation
  
  async increment(): Promise<number> {
    if (!this.impl) {
      throw new Error('Actor not initialized');
    }
    return this.impl.increment();
  }

  async decrement(): Promise<number> {
    if (!this.impl) {
      throw new Error('Actor not initialized');
    }
    return this.impl.decrement();
  }

  async getCount(): Promise<number> {
    if (!this.impl) {
      throw new Error('Actor not initialized');
    }
    return this.impl.getCount();
  }

  async reset(): Promise<void> {
    if (!this.impl) {
      throw new Error('Actor not initialized');
    }
    return this.impl.reset();
  }

  // Test method to verify DI is working
  async testDI(): Promise<object> {
    const { logger, config } = getCradle();
    logger.log('testDI method called - DI is working!');
    
    return {
      message: 'DI is working!',
      config: {
        maxCount: config.maxCount,
        minCount: config.minCount,
        defaultValue: config.defaultValue
      },
      actorId: this.actorIdString
    };
  }
}