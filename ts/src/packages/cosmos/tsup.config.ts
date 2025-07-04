import { defineConfig } from 'tsup'

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: {
    entry: 'src/index.ts'
  },
  clean: true,
  sourcemap: true,
  external: ['@azure/cosmos', '@sekiban/core']
})