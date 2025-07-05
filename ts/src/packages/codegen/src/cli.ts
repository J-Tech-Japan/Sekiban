#!/usr/bin/env node

import { readFile, writeFile, mkdir } from 'fs/promises';
import { dirname, resolve } from 'path';
import { Project } from 'ts-morph';
import { SchemaScanner } from './scanner.js';
import { CodeGenerator } from './generator.js';
import type { CliOptions, ScannerConfig, GeneratorConfig } from './types.js';

/**
 * CLI tool for generating domain registries
 */
export class SekibanCodegenCli {
  constructor(private options: CliOptions) {}

  async run(): Promise<void> {
    try {
      if (this.options.verbose) {
        console.log('🚀 Starting Sekiban codegen...');
      }

      // Load configuration
      const config = await this.loadConfig();

      // Create TypeScript project
      const project = new Project({
        tsConfigFilePath: 'tsconfig.json',
        skipAddingFilesFromTsConfig: false
      });

      // Create scanner with configuration
      const scanner = new SchemaScanner(project, config.scanner);

      if (this.options.verbose) {
        console.log('📂 Scanning for schema definitions...');
      }

      // Scan for schemas
      const scannedSchema = scanner.scanForSchemas();

      if (this.options.verbose) {
        console.log(`✅ Found ${scannedSchema.summary.totalEvents} events, ${scannedSchema.summary.totalCommands} commands, ${scannedSchema.summary.totalProjectors} projectors`);
      }

      // Create generator
      const generator = new CodeGenerator(config.generator);

      if (this.options.verbose) {
        console.log('🔧 Generating registry code...');
      }

      // Generate code
      const generatedCode = generator.generateCode(scannedSchema);

      if (this.options.dryRun) {
        console.log('📋 Generated code (dry run):');
        console.log(generatedCode.code);
        return;
      }

      // Write output file
      const outputPath = this.options.output || config.generator.outputFile;
      await this.ensureDirectoryExists(outputPath);
      await writeFile(outputPath, generatedCode.code, 'utf-8');

      if (this.options.verbose) {
        console.log(`💾 Generated registry written to: ${outputPath}`);
      }

      // Write declaration file if enabled
      if (generatedCode.declarations && config.generator.generateDeclarations) {
        const declarationPath = outputPath.replace(/\.ts$/, '.d.ts');
        await writeFile(declarationPath, generatedCode.declarations, 'utf-8');
        
        if (this.options.verbose) {
          console.log(`📝 Type declarations written to: ${declarationPath}`);
        }
      }

      console.log('✨ Domain registry generation complete!');

    } catch (error) {
      console.error('❌ Error during generation:', error);
      process.exit(1);
    }
  }

  private async loadConfig(): Promise<{
    scanner: ScannerConfig;
    generator: GeneratorConfig;
  }> {
    // Default configuration
    const defaultConfig = {
      scanner: {
        include: ['src/**/*.ts'],
        exclude: ['**/*.test.ts', '**/*.spec.ts', '**/node_modules/**'],
        outputPath: 'src/generated',
        baseDir: process.cwd()
      },
      generator: {
        outputFile: this.options.output || 'src/generated/domain-registry.ts',
        generateDeclarations: true,
        importStyle: 'relative' as const,
        includeComments: true,
        template: 'default' as const
      }
    };

    // Load custom configuration if specified
    if (this.options.config) {
      try {
        const configContent = await readFile(this.options.config, 'utf-8');
        const customConfig = JSON.parse(configContent);
        
        return {
          scanner: { ...defaultConfig.scanner, ...customConfig.scanner },
          generator: { ...defaultConfig.generator, ...customConfig.generator }
        };
      } catch (error) {
        console.warn(`⚠️  Could not load config file ${this.options.config}, using defaults`);
      }
    }

    return defaultConfig;
  }

  private async ensureDirectoryExists(filePath: string): Promise<void> {
    const dir = dirname(filePath);
    await mkdir(dir, { recursive: true });
  }
}

// Parse command line arguments
function parseArgs(): CliOptions {
  const args = process.argv.slice(2);
  const options: CliOptions = {};

  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    
    switch (arg) {
      case '--input':
      case '-i':
        options.input = args[++i];
        break;
      case '--output':
      case '-o':
        options.output = args[++i];
        break;
      case '--config':
      case '-c':
        options.config = args[++i];
        break;
      case '--watch':
      case '-w':
        options.watch = true;
        break;
      case '--verbose':
      case '-v':
        options.verbose = true;
        break;
      case '--dry-run':
      case '-d':
        options.dryRun = true;
        break;
      case '--help':
      case '-h':
        showHelp();
        process.exit(0);
        break;
      default:
        if (arg.startsWith('-')) {
          console.error(`Unknown option: ${arg}`);
          process.exit(1);
        }
    }
  }

  return options;
}

function showHelp(): void {
  console.log(`
Sekiban Domain Registry Generator

Usage: sekiban-codegen [options]

Options:
  -i, --input <dir>     Input directory to scan (default: current directory)
  -o, --output <file>   Output file for generated registry
  -c, --config <file>   Configuration file path
  -w, --watch          Watch mode for continuous generation
  -v, --verbose        Verbose logging
  -d, --dry-run        Preview generated code without writing files
  -h, --help           Show this help message

Examples:
  sekiban-codegen
  sekiban-codegen --output src/generated/registry.ts
  sekiban-codegen --config codegen.config.json --verbose
  sekiban-codegen --dry-run
`);
}

// Main execution
if (import.meta.url === `file://${process.argv[1]}`) {
  const options = parseArgs();
  const cli = new SekibanCodegenCli(options);
  cli.run();
}