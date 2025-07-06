import { defineConfig } from 'tsup';

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: false,
  clean: false,
  sourcemap: true,
  external: ['@sekiban/core', '@dapr/dapr', 'neverthrow'],
  onSuccess: 'echo "Build completed successfully"'
});