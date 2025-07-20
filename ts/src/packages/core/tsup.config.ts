import { defineConfig } from 'tsup';

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: true,
  clean: true,
  sourcemap: true,
  splitting: false,
  minify: false,
  external: ['neverthrow', 'zod'],
  noExternal: ['uuid'],
  treeshake: false,
  outDir: 'dist',
  target: 'node18',
  platform: 'node',
  bundle: true,
  esbuildOptions(options) {
    options.platform = 'node'
    options.target = 'node18'
  }
});