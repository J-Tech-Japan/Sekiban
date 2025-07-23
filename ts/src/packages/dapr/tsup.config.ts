import { defineConfig } from 'tsup';

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: false, // Disable dts generation - we'll use tsc directly
  clean: true,
  sourcemap: true,
  minify: false,
  keepNames: true,
  external: ['@sekiban/core', '@dapr/dapr', 'neverthrow', 'awilix']
});