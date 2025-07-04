import { defineConfig } from 'tsup';

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: false,
  clean: true,
  sourcemap: true,
  splitting: false,
  minify: false,
  external: ['neverthrow', 'uuid', 'zod'],
  treeshake: true,
  outDir: 'dist',
  target: 'node18',
  platform: 'node',
  shims: false,
});