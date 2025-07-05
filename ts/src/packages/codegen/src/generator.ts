import type { 
  ScannedSchema,
  GeneratorConfig,
  GeneratedCode
} from './types.js';

/**
 * Code generator that creates TypeScript registry files from scanned schema definitions
 */
export class CodeGenerator {
  private config: Required<GeneratorConfig>;

  constructor(config: GeneratorConfig) {
    this.config = {
      outputFile: config.outputFile,
      generateDeclarations: config.generateDeclarations ?? true,
      importStyle: config.importStyle ?? 'relative',
      includeComments: config.includeComments ?? true,
      template: config.template ?? 'default'
    };
  }

  /**
   * Generate TypeScript code from scanned schema
   */
  generateCode(scannedSchema: ScannedSchema): GeneratedCode {
    const sections: string[] = [];

    // Add header comments
    if (this.config.includeComments && this.config.template !== 'minimal') {
      sections.push(this.generateHeaderComments(scannedSchema));
    }

    // Add imports
    sections.push(this.generateImports(scannedSchema));

    // Add event registry
    sections.push(this.generateEventRegistry(scannedSchema.events));

    // Add command registry  
    sections.push(this.generateCommandRegistry(scannedSchema.commands));

    // Add projector registry
    sections.push(this.generateProjectorRegistry(scannedSchema.projectors));

    // Add type exports
    sections.push(this.generateTypeExports());

    // Add union types
    sections.push(this.generateUnionTypes(scannedSchema));

    // Add main domain registry
    sections.push(this.generateDomainRegistry());

    // Add advanced TypeScript types
    if (this.config.template === 'default' || this.config.template === 'comprehensive') {
      sections.push(this.generateAdvancedTypes());
    }

    // Add generation metadata
    if (this.config.includeComments) {
      sections.push(this.generateMetadataComments(scannedSchema));
    }

    const code = sections.filter(section => section.trim()).join('\n\n');

    return {
      code,
      declarations: this.config.generateDeclarations ? this.generateDeclarations(code) : undefined
    };
  }

  /**
   * Generate header comments
   */
  private generateHeaderComments(scannedSchema: ScannedSchema): string {
    const timestamp = new Date().toISOString();
    
    return `// Generated domain registry
// DO NOT EDIT - This file is auto-generated
// Generated on: ${timestamp}`;
  }

  /**
   * Generate import statements
   */
  private generateImports(scannedSchema: ScannedSchema): string {
    const imports: string[] = [];

    // Import events
    for (const event of scannedSchema.events) {
      imports.push(`import { ${event.name} } from '${event.importPath}';`);
    }

    // Import commands
    for (const command of scannedSchema.commands) {
      imports.push(`import { ${command.name} } from '${command.importPath}';`);
    }

    // Import projectors
    for (const projector of scannedSchema.projectors) {
      imports.push(`import { ${projector.name} } from '${projector.importPath}';`);
    }

    return imports.join('\n');
  }

  /**
   * Generate event registry object
   */
  private generateEventRegistry(events: ScannedSchema['events']): string {
    if (events.length === 0) {
      return 'export const events = {} as const;';
    }

    const eventEntries = events.map(event => `  ${event.name},`).join('\n');
    
    return `export const events = {
${eventEntries}
} as const;`;
  }

  /**
   * Generate command registry object
   */
  private generateCommandRegistry(commands: ScannedSchema['commands']): string {
    if (commands.length === 0) {
      return 'export const commands = {} as const;';
    }

    const commandEntries = commands.map(command => `  ${command.name},`).join('\n');
    
    return `export const commands = {
${commandEntries}
} as const;`;
  }

  /**
   * Generate projector registry object
   */
  private generateProjectorRegistry(projectors: ScannedSchema['projectors']): string {
    if (projectors.length === 0) {
      return 'export const projectors = {} as const;';
    }

    const projectorEntries = projectors.map(projector => `  ${projector.name},`).join('\n');
    
    return `export const projectors = {
${projectorEntries}
} as const;`;
  }

  /**
   * Generate type exports for registries
   */
  private generateTypeExports(): string {
    return `export type EventTypes = typeof events;
export type CommandTypes = typeof commands;
export type ProjectorTypes = typeof projectors;`;
  }

  /**
   * Generate union types for domain objects
   */
  private generateUnionTypes(scannedSchema: ScannedSchema): string {
    const lines: string[] = [];

    // Generate event union type
    if (scannedSchema.events.length === 0) {
      lines.push('export type DomainEvent = never;');
    } else {
      const eventTypes = scannedSchema.events
        .map(event => `ReturnType<typeof ${event.name}.create>`)
        .join(' | ');
      lines.push(`export type DomainEvent = ${eventTypes};`);
    }

    // Generate command union type
    if (scannedSchema.commands.length === 0) {
      lines.push('export type DomainCommand = never;');
    } else {
      const commandTypes = scannedSchema.commands
        .map(command => `ReturnType<typeof ${command.name}.create>`)
        .join(' | ');
      lines.push(`export type DomainCommand = ${commandTypes};`);
    }

    return lines.join('\n');
  }

  /**
   * Generate main domain registry export
   */
  private generateDomainRegistry(): string {
    return `export const domainRegistry = {
  events,
  commands,
  projectors,
} as const;`;
  }

  /**
   * Generate advanced TypeScript types
   */
  private generateAdvancedTypes(): string {
    return `export type DomainRegistryType = typeof domainRegistry;

export type EventTypeLookup = {
  [K in keyof EventTypes]: ReturnType<EventTypes[K]["create"]>
};

export type CommandTypeLookup = {
  [K in keyof CommandTypes]: ReturnType<CommandTypes[K]["create"]>
};`;
  }

  /**
   * Generate metadata comments
   */
  private generateMetadataComments(scannedSchema: ScannedSchema): string {
    const { summary } = scannedSchema;
    
    return `/**
 * Generation metadata:
 * - Total files scanned: ${summary.totalFiles}
 * - Total events: ${summary.totalEvents}
 * - Total commands: ${summary.totalCommands}
 * - Total projectors: ${summary.totalProjectors}
 * - Scan duration: ${summary.scanDurationMs}ms
 */`;
  }

  /**
   * Generate TypeScript declaration file content
   */
  private generateDeclarations(code: string): string {
    // Extract type declarations from the generated code
    const lines = code.split('\n');
    const declarationLines = lines.filter(line => 
      line.trim().startsWith('export type') || 
      line.trim().startsWith('export interface') ||
      line.trim().startsWith('export declare')
    );

    return declarationLines.join('\n');
  }
}