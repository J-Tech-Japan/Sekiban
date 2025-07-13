import { defineConfig } from 'tsup';

export default defineConfig([
  // ESM build
  {
    entry: ['src/index.ts'],
    format: 'esm',
    dts: false,
    clean: true,
    sourcemap: true,
    splitting: false,
    minify: false,
    external: ['neverthrow', 'zod'],
    noExternal: ['uuid'],
    treeshake: false,
    outDir: 'dist',
    outExtension: () => ({ js: '.mjs' }),
    target: 'node18',
    platform: 'node',
    bundle: true,
    esbuildOptions(options) {
      options.platform = 'node'
      options.target = 'node18'
      options.format = 'esm'
    }
  },
  // CJS build
  {
    entry: ['src/index.ts'],
    format: 'cjs',
    dts: false,
    clean: false,
    sourcemap: true,
    splitting: false,
    minify: false,
    external: ['neverthrow', 'zod'],
    noExternal: ['uuid'],
    treeshake: false,
    outDir: 'dist',
    outExtension: () => ({ js: '.js' }),
    target: 'node18',
    platform: 'node',
    bundle: true,
    onSuccess: 'echo "Build completed"'
  }
]);