import { Project, SourceFile, CallExpression, Node, SyntaxKind } from 'ts-morph';
import * as path from 'path';
import type { 
  ScannedSchema, 
  ScannedEvent, 
  ScannedCommand, 
  ScannedProjector, 
  ScannerConfig,
  ScanSummary 
} from './types.js';

/**
 * Schema scanner that analyzes TypeScript source files to find
 * defineEvent, defineCommand, and defineProjector calls.
 */
export class SchemaScanner {
  private config: Required<ScannerConfig>;

  constructor(
    private project: Project,
    config: ScannerConfig = {}
  ) {
    this.config = {
      include: config.include || ['**/*.ts'],
      exclude: config.exclude || ['**/*.test.ts', '**/*.spec.ts', '**/node_modules/**'],
      outputPath: config.outputPath || 'src/generated',
      baseDir: config.baseDir || ''
    };
  }

  /**
   * Scan all source files for schema definitions
   */
  scanForSchemas(): ScannedSchema {
    const startTime = Date.now();
    
    const events: ScannedEvent[] = [];
    const commands: ScannedCommand[] = [];
    const projectors: ScannedProjector[] = [];
    
    const sourceFiles = this.getRelevantSourceFiles();

    for (const sourceFile of sourceFiles) {
      try {
        events.push(...this.scanFileForEvents(sourceFile));
        commands.push(...this.scanFileForCommands(sourceFile));
        projectors.push(...this.scanFileForProjectors(sourceFile));
      } catch (error) {
        // Log error but continue scanning other files
        console.warn(`Error scanning file ${sourceFile.getFilePath()}:`, error);
      }
    }

    const endTime = Date.now();
    
    return {
      events,
      commands,
      projectors,
      summary: {
        totalEvents: events.length,
        totalCommands: commands.length,
        totalProjectors: projectors.length,
        totalFiles: sourceFiles.length,
        scanDurationMs: endTime - startTime
      }
    };
  }

  /**
   * Get source files that match include/exclude patterns
   */
  private getRelevantSourceFiles(): SourceFile[] {
    const allFiles = this.project.getSourceFiles();
    
    return allFiles.filter(file => {
      const filePath = file.getFilePath();
      
      // Check exclude patterns first
      for (const excludePattern of this.config.exclude) {
        if (this.matchesPattern(filePath, excludePattern)) {
          return false;
        }
      }
      
      // Check include patterns
      for (const includePattern of this.config.include) {
        if (this.matchesPattern(filePath, includePattern)) {
          return true;
        }
      }
      
      return false;
    });
  }

  /**
   * Simple pattern matching for file paths
   */
  private matchesPattern(filePath: string, pattern: string): boolean {
    // Normalize the file path by removing leading slash
    const normalizedPath = filePath.startsWith('/') ? filePath.slice(1) : filePath;
    
    // Handle common patterns
    if (pattern === '**/*.ts') {
      return normalizedPath.endsWith('.ts');
    }
    if (pattern === '**/*.test.ts') {
      return normalizedPath.endsWith('.test.ts');
    }
    if (pattern === '**/*.spec.ts') {
      return normalizedPath.endsWith('.spec.ts');
    }
    if (pattern === 'domain/**/*.ts') {
      return normalizedPath.startsWith('domain/') && normalizedPath.endsWith('.ts');
    }
    
    // Handle other patterns with basic glob support
    const regexPattern = pattern
      .replace(/\./g, '\\.')  // Escape dots
      .replace(/\*\*/g, '.*') // ** matches any characters including path separators
      .replace(/\*/g, '[^/]*') // * matches any characters except path separator
      .replace(/\?/g, '.'); // ? matches any single character
    
    const regex = new RegExp(`^${regexPattern}$`);
    return regex.test(normalizedPath);
  }

  /**
   * Scan a file for defineEvent calls
   */
  private scanFileForEvents(sourceFile: SourceFile): ScannedEvent[] {
    const events: ScannedEvent[] = [];
    
    // Find all call expressions
    const callExpressions = sourceFile.getDescendantsOfKind(SyntaxKind.CallExpression);
    
    for (const call of callExpressions) {
      if (this.isDefineEventCall(call)) {
        const event = this.extractEventInfo(call, sourceFile);
        if (event) {
          events.push(event);
        }
      }
    }
    
    return events;
  }

  /**
   * Scan a file for defineCommand calls
   */
  private scanFileForCommands(sourceFile: SourceFile): ScannedCommand[] {
    const commands: ScannedCommand[] = [];
    
    const callExpressions = sourceFile.getDescendantsOfKind(SyntaxKind.CallExpression);
    
    for (const call of callExpressions) {
      if (this.isDefineCommandCall(call)) {
        const command = this.extractCommandInfo(call, sourceFile);
        if (command) {
          commands.push(command);
        }
      }
    }
    
    return commands;
  }

  /**
   * Scan a file for defineProjector calls
   */
  private scanFileForProjectors(sourceFile: SourceFile): ScannedProjector[] {
    const projectors: ScannedProjector[] = [];
    
    const callExpressions = sourceFile.getDescendantsOfKind(SyntaxKind.CallExpression);
    
    for (const call of callExpressions) {
      if (this.isDefineProjectorCall(call)) {
        const projector = this.extractProjectorInfo(call, sourceFile);
        if (projector) {
          projectors.push(projector);
        }
      }
    }
    
    return projectors;
  }

  /**
   * Check if a call expression is defineEvent
   */
  private isDefineEventCall(call: CallExpression): boolean {
    const expression = call.getExpression();
    return expression.getText() === 'defineEvent';
  }

  /**
   * Check if a call expression is defineCommand
   */
  private isDefineCommandCall(call: CallExpression): boolean {
    const expression = call.getExpression();
    return expression.getText() === 'defineCommand';
  }

  /**
   * Check if a call expression is defineProjector
   */
  private isDefineProjectorCall(call: CallExpression): boolean {
    const expression = call.getExpression();
    return expression.getText() === 'defineProjector';
  }

  /**
   * Extract event information from defineEvent call
   */
  private extractEventInfo(call: CallExpression, sourceFile: SourceFile): ScannedEvent | null {
    const variableName = this.getVariableName(call);
    if (!variableName) return null;

    // Check if the 'type' property exists at all
    const hasTypeProperty = this.hasTypeProperty(call);
    if (!hasTypeProperty) return null; // Require type property to be present
    
    const typeValue = this.extractTypeProperty(call);
    const filePath = sourceFile.getFilePath();
    
    return {
      name: variableName,
      type: typeValue || variableName, // Fallback to variable name if type is not a literal
      filePath,
      importPath: this.calculateImportPath(filePath)
    };
  }

  /**
   * Extract command information from defineCommand call
   */
  private extractCommandInfo(call: CallExpression, sourceFile: SourceFile): ScannedCommand | null {
    const variableName = this.getVariableName(call);
    if (!variableName) return null;

    // Check if the 'type' property exists at all
    const hasTypeProperty = this.hasTypeProperty(call);
    if (!hasTypeProperty) return null; // Require type property to be present
    
    const typeValue = this.extractTypeProperty(call);
    const filePath = sourceFile.getFilePath();
    
    return {
      name: variableName,
      type: typeValue || variableName, // Fallback to variable name if type is not a literal
      filePath,
      importPath: this.calculateImportPath(filePath)
    };
  }

  /**
   * Extract projector information from defineProjector call
   */
  private extractProjectorInfo(call: CallExpression, sourceFile: SourceFile): ScannedProjector | null {
    const variableName = this.getVariableName(call);
    if (!variableName) return null;

    const aggregateType = this.extractAggregateTypeProperty(call);
    if (!aggregateType) return null;

    const filePath = sourceFile.getFilePath();
    
    return {
      name: variableName,
      aggregateType,
      filePath,
      importPath: this.calculateImportPath(filePath)
    };
  }

  /**
   * Get the variable name that the call is assigned to
   */
  private getVariableName(call: CallExpression): string | null {
    const parent = call.getParent();
    
    // Check if it's a variable declaration: const EventName = defineEvent(...)
    if (Node.isVariableDeclaration(parent)) {
      return parent.getName();
    }
    
    // Check if it's an export assignment: export const EventName = defineEvent(...)
    const grandParent = parent?.getParent();
    if (Node.isVariableDeclaration(grandParent)) {
      return grandParent.getName();
    }
    
    return null;
  }

  /**
   * Check if the 'type' property exists in the call's object literal
   */
  private hasTypeProperty(call: CallExpression): boolean {
    const args = call.getArguments();
    if (args.length === 0) return false;

    let firstArg = args[0];
    
    // Handle object with "as const"
    if (Node.isAsExpression(firstArg)) {
      firstArg = firstArg.getExpression();
    }
    
    if (!Node.isObjectLiteralExpression(firstArg)) return false;

    for (const property of firstArg.getProperties()) {
      if (Node.isPropertyAssignment(property)) {
        const name = property.getName();
        if (name === 'type') {
          return true;
        }
      }
    }

    return false;
  }

  /**
   * Extract the 'type' property from the call's object literal
   */
  private extractTypeProperty(call: CallExpression): string | null {
    const args = call.getArguments();
    if (args.length === 0) return null;

    let firstArg = args[0];
    
    // Handle object with "as const"
    if (Node.isAsExpression(firstArg)) {
      firstArg = firstArg.getExpression();
    }
    
    if (!Node.isObjectLiteralExpression(firstArg)) return null;

    for (const property of firstArg.getProperties()) {
      if (Node.isPropertyAssignment(property)) {
        const name = property.getName();
        if (name === 'type') {
          const initializer = property.getInitializer();
          if (Node.isStringLiteral(initializer)) {
            return initializer.getLiteralValue();
          }
          // Handle 'string' as const
          if (Node.isAsExpression(initializer)) {
            const expression = initializer.getExpression();
            if (Node.isStringLiteral(expression)) {
              return expression.getLiteralValue();
            }
          }
        }
      }
    }

    return null;
  }

  /**
   * Extract the 'aggregateType' property from the call's object literal
   */
  private extractAggregateTypeProperty(call: CallExpression): string | null {
    const args = call.getArguments();
    if (args.length === 0) return null;

    const firstArg = args[0];
    if (!Node.isObjectLiteralExpression(firstArg)) return null;

    for (const property of firstArg.getProperties()) {
      if (Node.isPropertyAssignment(property)) {
        const name = property.getName();
        if (name === 'aggregateType') {
          const initializer = property.getInitializer();
          if (Node.isStringLiteral(initializer)) {
            return initializer.getLiteralValue();
          }
        }
      }
    }

    return null;
  }

  /**
   * Calculate import path relative to the output directory
   */
  private calculateImportPath(filePath: string): string {
    // Normalize file path by removing leading slash
    const normalizedPath = filePath.startsWith('/') ? filePath.slice(1) : filePath;
    
    // Calculate relative path from output directory to the source file
    const outputDir = this.config.outputPath; // e.g., "src/generated"
    const relativePath = path.relative(outputDir, normalizedPath);
    
    // Remove .ts extension and add .js extension
    const jsPath = relativePath.replace(/\.ts$/, '.js');
    
    // Ensure it starts with ./ or ../
    if (!jsPath.startsWith('./') && !jsPath.startsWith('../')) {
      return './' + jsPath;
    }
    
    return jsPath;
  }
}