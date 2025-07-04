import { defineConfig } from 'vitest/config';
import path from 'path';

export default defineConfig({
  test: {
    globals: true,
    environment: 'node',
    setupFiles: ['./tests/setup.ts'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      exclude: [
        'node_modules/**',
        'dist/**',
        '**/*.d.ts',
        '**/*.config.*',
        '**/mockData.ts'
      ]
    }
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
      '@sekiban/core': path.resolve(__dirname, '../../../../src/packages/core/src'),
      '@sekiban/dapr': path.resolve(__dirname, '../../../../src/packages/dapr/src'),
      '@sekiban/dapr-sample-domain': path.resolve(__dirname, '../../packages/domain/src')
    }
  }
});