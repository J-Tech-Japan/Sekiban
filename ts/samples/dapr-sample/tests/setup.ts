import { beforeAll, afterAll } from 'vitest';

beforeAll(async () => {
  // Set up test environment
  process.env.DATABASE_URL = 'postgresql://sekiban:sekiban@localhost:5432/sekiban_test';
  process.env.POSTGRES_HOST = 'localhost';
  process.env.POSTGRES_PORT = '5432';
  process.env.POSTGRES_DATABASE = 'sekiban_test';
  process.env.POSTGRES_USER = 'sekiban';
  process.env.POSTGRES_PASSWORD = 'sekiban';
  process.env.NODE_ENV = 'test';
  
  console.log('Test environment configured');
}, 30000);

afterAll(async () => {
  console.log('Test cleanup completed');
}, 5000);