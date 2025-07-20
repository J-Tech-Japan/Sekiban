import { defineConfig } from 'tsup'

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: false, // Disable dts generation as we'll use tsc directly
  clean: true,
  sourcemap: true,
  external: ['pg', '@sekiban/core'],
  onSuccess: 'npx tsc --emitDeclarationOnly --declaration --declarationMap && echo "Build completed successfully"'
})