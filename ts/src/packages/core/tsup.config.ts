import { defineConfig } from 'tsup';

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: false, // Temporarily disable dts generation
  clean: false, // Don't clean to preserve JS files
  sourcemap: true,
  splitting: false,
  minify: false,
  external: ['neverthrow', 'uuid', 'zod'],
  treeshake: true,
  outDir: 'dist',
  target: 'node18',
  platform: 'node',
  shims: false,
  onSuccess: 'echo "Build completed without types - run tsc separately for type definitions"'
});