import { defineConfig } from 'tsup';

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['cjs', 'esm'],
  dts: {
    resolve: true,
    compilerOptions: {
      moduleResolution: 'node',
      allowImportingTsExtensions: false
    }
  },
  clean: true,
  sourcemap: true,
  external: ['neverthrow', 'zod'],
  noExternal: ['uuid']
});