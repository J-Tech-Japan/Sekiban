import type { SekibanConfig } from '../../src/infrastructure/simple-sekiban-executor';

export function createTestConfig(connectionString: string): SekibanConfig {
  return {
    storage: {
      type: 'postgres',
      postgres: {
        connectionString,
        maxConnections: 10,
        idleTimeoutMillis: 30000,
        connectionTimeoutMillis: 5000
      }
    },
    dapr: {
      httpPort: 3500,
      grpcPort: 50001,
      stateStoreName: 'test-state-store',
      pubSubName: 'test-pubsub',
      appId: 'sekiban-dapr-test'
    },
    server: {
      port: 0, // Use random port for tests
      host: 'localhost'
    },
    environment: 'test'
  };
}