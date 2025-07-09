import { AbstractActor } from '@dapr/dapr';

/**
 * Simple counter actor that maintains a count in state
 */
export class CounterActor extends AbstractActor {
  private readonly STATE_KEY = 'count';

  async increment(): Promise<number> {
    console.log(`[CounterActor ${this.id}] increment() called`);
    
    const stateManager = await this.getStateManager();
    const [hasState, currentCount] = await stateManager.tryGetState<number>(this.STATE_KEY);
    
    const count = hasState ? currentCount! : 0;
    const newCount = count + 1;
    
    await stateManager.setState(this.STATE_KEY, newCount);
    console.log(`[CounterActor ${this.id}] Count incremented from ${count} to ${newCount}`);
    
    return newCount;
  }

  async decrement(): Promise<number> {
    console.log(`[CounterActor ${this.id}] decrement() called`);
    
    const stateManager = await this.getStateManager();
    const [hasState, currentCount] = await stateManager.tryGetState<number>(this.STATE_KEY);
    
    const count = hasState ? currentCount! : 0;
    const newCount = count - 1;
    
    await stateManager.setState(this.STATE_KEY, newCount);
    console.log(`[CounterActor ${this.id}] Count decremented from ${count} to ${newCount}`);
    
    return newCount;
  }

  async getCount(): Promise<number> {
    console.log(`[CounterActor ${this.id}] getCount() called`);
    
    const stateManager = await this.getStateManager();
    const [hasState, currentCount] = await stateManager.tryGetState<number>(this.STATE_KEY);
    
    const count = hasState ? currentCount! : 0;
    console.log(`[CounterActor ${this.id}] Current count is ${count}`);
    
    return count;
  }

  async reset(): Promise<void> {
    console.log(`[CounterActor ${this.id}] reset() called`);
    
    const stateManager = await this.getStateManager();
    await stateManager.setState(this.STATE_KEY, 0);
    
    console.log(`[CounterActor ${this.id}] Count reset to 0`);
  }

  async onActivate(): Promise<void> {
    console.log(`[CounterActor ${this.id}] Activated`);
  }

  async onDeactivate(): Promise<void> {
    console.log(`[CounterActor ${this.id}] Deactivated`);
  }
}