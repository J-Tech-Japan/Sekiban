import { defineConfig } from 'tsup';

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: false,
  clean: false,
  sourcemap: true,
  minify: false,
  keepNames: true,
  external: ['@sekiban/core', '@dapr/dapr', 'neverthrow', 'awilix'],
  onSuccess: 'echo "Build completed successfully"'
});