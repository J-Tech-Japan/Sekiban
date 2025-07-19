import { defineConfig } from 'tsup'

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: {
    // Skip type checking during build
    compilerOptions: {
      skipLibCheck: true,
      skipDefaultLibCheck: true
    }
  },
  clean: true,
  sourcemap: true,
  external: ['pg', '@sekiban/core']
})