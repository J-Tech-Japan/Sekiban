/**
 * @sekiban/codegen - Code generation tools for Sekiban TypeScript schema registry
 */

export { SchemaScanner } from './scanner.js';
export { CodeGenerator } from './generator.js';
export { SekibanCodegenCli } from './cli.js';

export type {
  ScannedSchema,
  ScannedEvent,
  ScannedCommand,
  ScannedProjector,
  ScanSummary,
  ScannerConfig,
  GeneratorConfig,
  GeneratedCode,
  CliOptions,
  CodegenError
} from './types.js';

// Re-export for convenience
export type { Project } from 'ts-morph';