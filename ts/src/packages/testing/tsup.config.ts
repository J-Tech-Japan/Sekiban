import { defineConfig } from 'tsup'

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: false, // Temporarily disable DTS due to tsup issues
  clean: true,
  sourcemap: true,
  external: ['@sekiban/core', 'vitest'],
  splitting: false
})