import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    globals: true,
    environment: 'node',
    
    // TDD-optimized settings
    watch: true,
    watchExclude: ['**/node_modules/**', '**/dist/**'],
    
    // Fast feedback for TDD Red-Green-Refactor cycle
    testTimeout: 5000,
    hookTimeout: 5000,
    
    // Coverage for quality assurance
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      exclude: [
        'node_modules/',
        'dist/',
        '**/*.d.ts',
        '**/*.config.*',
        '**/index.ts', // barrel exports
      ],
      thresholds: {
        lines: 90,
        functions: 90,
        branches: 85,
        statements: 90
      }
    },
    
    // Better test organization
    include: ['src/**/*.test.ts'],
    exclude: [
      '**/node_modules/**',
      '**/dist/**',
      '**/*.d.ts'
    ],
    
    // Helpful for debugging during TDD
    logHeapUsage: true,
    
    // Parallel execution for faster feedback
    threads: true,
    maxThreads: 4,
    
    // Reporter settings optimized for TDD workflow
    reporter: ['verbose'],
    
    // Useful for TDD - show which tests are slow
    slowTestThreshold: 1000
  }
});