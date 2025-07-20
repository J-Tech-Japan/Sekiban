import { defineConfig } from 'tsup'

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: true, // Enable dts generation
  clean: true,
  sourcemap: true,
  external: ['pg', '@sekiban/core']
})