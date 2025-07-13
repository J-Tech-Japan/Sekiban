import { createContainer, asValue, InjectionMode, AwilixContainer } from 'awilix';

// Define the shape of our dependencies
export interface CounterDependencies {
  logger: Logger;
  counterService: CounterService;
  config: AppConfig;
}

// Simple logger interface
export interface Logger {
  log(message: string): void;
  error(message: string, error?: any): void;
}

// Counter service interface
export interface CounterService {
  calculateNewValue(current: number, operation: 'increment' | 'decrement'): number;
  validateCount(count: number): boolean;
}

// App configuration
export interface AppConfig {
  maxCount: number;
  minCount: number;
  defaultValue: number;
}

// Container instance
let container: AwilixContainer<CounterDependencies>;

// Initialize the DI container
export function initializeContainer(): void {
  // Create container with proxy injection mode
  container = createContainer<CounterDependencies>({
    injectionMode: InjectionMode.PROXY,
    strict: true
  });

  // Register dependencies
  container.register({
    // Simple console logger
    logger: asValue<Logger>({
      log: (message: string) => console.log(`[DI Logger] ${message}`),
      error: (message: string, error?: any) => console.error(`[DI Logger] ERROR: ${message}`, error)
    }),

    // Counter service with business logic
    counterService: asValue<CounterService>({
      calculateNewValue: (current: number, operation: 'increment' | 'decrement') => {
        return operation === 'increment' ? current + 1 : current - 1;
      },
      validateCount: (count: number) => {
        const config = container.cradle.config;
        return count >= config.minCount && count <= config.maxCount;
      }
    }),

    // Configuration
    config: asValue<AppConfig>({
      maxCount: 100,
      minCount: -100,
      defaultValue: 0
    })
  });

  console.log('✅ DI Container initialized with Awilix');
}

// Get the container cradle (resolved dependencies)
export function getCradle(): CounterDependencies {
  if (!container) {
    throw new Error('DI Container not initialized. Call initializeContainer() first.');
  }
  return container.cradle;
}

// Get the container instance
export function getContainer(): AwilixContainer<CounterDependencies> {
  if (!container) {
    throw new Error('DI Container not initialized. Call initializeContainer() first.');
  }
  return container;
}