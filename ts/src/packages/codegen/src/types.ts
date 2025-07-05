/**
 * Information about a scanned event schema
 */
export interface ScannedEvent {
  /** Variable name in the source code */
  name: string;
  /** Event type from the schema definition */
  type: string;
  /** Source file path */
  filePath: string;
  /** Import path for the generated registry */
  importPath: string;
}

/**
 * Information about a scanned command schema
 */
export interface ScannedCommand {
  /** Variable name in the source code */
  name: string;
  /** Command type from the schema definition */
  type: string;
  /** Source file path */
  filePath: string;
  /** Import path for the generated registry */
  importPath: string;
}

/**
 * Information about a scanned projector
 */
export interface ScannedProjector {
  /** Variable name in the source code */
  name: string;
  /** Aggregate type from the projector definition */
  aggregateType: string;
  /** Source file path */
  filePath: string;
  /** Import path for the generated registry */
  importPath: string;
}

/**
 * Complete scan result
 */
export interface ScannedSchema {
  events: ScannedEvent[];
  commands: ScannedCommand[];
  projectors: ScannedProjector[];
  summary: ScanSummary;
}

/**
 * Summary of scan results
 */
export interface ScanSummary {
  totalEvents: number;
  totalCommands: number;
  totalProjectors: number;
  totalFiles: number;
  scanDurationMs: number;
}

/**
 * Scanner configuration options
 */
export interface ScannerConfig {
  /** Glob patterns for files to include */
  include?: string[];
  /** Glob patterns for files to exclude */
  exclude?: string[];
  /** Output path for generated files */
  outputPath?: string;
  /** Base directory for calculating import paths */
  baseDir?: string;
}

/**
 * Code generator configuration
 */
export interface GeneratorConfig {
  /** Output file path for the generated registry */
  outputFile: string;
  /** Whether to generate TypeScript declaration files */
  generateDeclarations?: boolean;
  /** Import path style (relative, absolute, etc.) */
  importStyle?: 'relative' | 'absolute';
  /** Whether to include comments in generated code */
  includeComments?: boolean;
  /** Template to use for code generation */
  template?: 'default' | 'minimal' | 'comprehensive';
}

/**
 * CLI command options
 */
export interface CliOptions {
  /** Input directory to scan */
  input?: string;
  /** Output file for generated registry */
  output?: string;
  /** Configuration file path */
  config?: string;
  /** Watch mode for continuous generation */
  watch?: boolean;
  /** Verbose logging */
  verbose?: boolean;
  /** Dry run without writing files */
  dryRun?: boolean;
}

/**
 * Generated code structure
 */
export interface GeneratedCode {
  /** Generated TypeScript code */
  code: string;
  /** Generated type declarations */
  declarations?: string;
  /** Source map for debugging */
  sourceMap?: string;
}

/**
 * Error information during scanning or generation
 */
export interface CodegenError {
  /** Error message */
  message: string;
  /** File where error occurred */
  file?: string;
  /** Line number where error occurred */
  line?: number;
  /** Column number where error occurred */
  column?: number;
  /** Error code for categorization */
  code?: string;
}