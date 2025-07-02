import { defineConfig } from 'tsup';

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: true,
  clean: true,
  sourcemap: true,
  splitting: false,
  minify: false,
  external: ['neverthrow', 'uuid'],
  treeshake: true,
  outDir: 'dist',
  target: 'node18',
  platform: 'neutral',
  shims: false,
});