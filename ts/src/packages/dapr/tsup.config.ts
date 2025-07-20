import { defineConfig } from 'tsup';

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: false, // Disable dts generation as we'll use tsc directly
  clean: false,
  sourcemap: true,
  minify: false,
  keepNames: true,
  external: ['@sekiban/core', '@dapr/dapr', 'neverthrow', 'awilix'],
  onSuccess: 'npx tsc --emitDeclarationOnly --declaration --declarationMap && echo "Build completed successfully"'
});