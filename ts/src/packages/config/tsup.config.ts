import { defineConfig } from 'tsup'

export default defineConfig({
  entry: {
    index: 'src/index.ts',
    'runtime/index': 'src/runtime/index.ts'
  },
  format: ['cjs', 'esm'],
  dts: false,  // Disable dts generation due to tsconfig issues
  clean: true,
  sourcemap: true,
  external: [
    '@sekiban/core',
    '@sekiban/postgres', 
    '@sekiban/cosmos',
    'pg',
    '@azure/cosmos'
  ],
  splitting: false
})