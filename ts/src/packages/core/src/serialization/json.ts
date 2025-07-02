import { Result, ok, err } from 'neverthrow';
import { SerializationError } from '../result';

/**
 * Interface for JSON serializers
 */
export interface IJsonSerializer {
  /**
   * Serializes an object to JSON string
   */
  serialize<T>(value: T): Result<string, SerializationError>;
  
  /**
   * Deserializes a JSON string to an object
   */
  deserialize<T>(json: string, type?: new(...args: any[]) => T): Result<T, SerializationError>;
}

/**
 * Default JSON serializer implementation
 */
export class DefaultJsonSerializer implements IJsonSerializer {
  private typeRegistry = new Map<string, new(...args: any[]) => any>();

  /**
   * Registers a type for deserialization
   */
  registerType<T>(typeName: string, type: new(...args: any[]) => T): void {
    this.typeRegistry.set(typeName, type);
  }

  serialize<T>(value: T): Result<string, SerializationError> {
    try {
      const json = JSON.stringify(value, this.replacer);
      return ok(json);
    } catch (error) {
      return err(new SerializationError('serialize', 
        error instanceof Error ? error.message : 'Unknown error'));
    }
  }

  deserialize<T>(json: string, type?: new(...args: any[]) => T): Result<T, SerializationError> {
    try {
      const parsed = JSON.parse(json, this.reviver);
      
      if (type && !(parsed instanceof type)) {
        // Try to construct the type if it's not already an instance
        return ok(Object.assign(new type(), parsed));
      }
      
      return ok(parsed);
    } catch (error) {
      return err(new SerializationError('deserialize', 
        error instanceof Error ? error.message : 'Unknown error'));
    }
  }

  /**
   * JSON replacer function for handling special types
   */
  private replacer = (key: string, value: any): any => {
    // Handle Date objects
    if (value instanceof Date) {
      return {
        $type: 'Date',
        $value: value.toISOString(),
      };
    }
    
    // Handle Map objects
    if (value instanceof Map) {
      return {
        $type: 'Map',
        $value: Array.from(value.entries()),
      };
    }
    
    // Handle Set objects
    if (value instanceof Set) {
      return {
        $type: 'Set',
        $value: Array.from(value),
      };
    }
    
    // Add type information for registered types
    if (value && typeof value === 'object' && value.constructor) {
      const typeName = this.getTypeName(value.constructor);
      if (typeName) {
        return {
          ...value,
          $type: typeName,
        };
      }
    }
    
    return value;
  };

  /**
   * JSON reviver function for handling special types
   */
  private reviver = (key: string, value: any): any => {
    if (value && typeof value === 'object' && value.$type) {
      const { $type, $value, ...rest } = value;
      
      switch ($type) {
        case 'Date':
          return new Date($value);
          
        case 'Map':
          return new Map($value);
          
        case 'Set':
          return new Set($value);
          
        default:
          // Try to find registered type
          const RegisteredType = this.typeRegistry.get($type);
          if (RegisteredType) {
            return Object.assign(new RegisteredType(), rest);
          }
      }
    }
    
    return value;
  };

  /**
   * Gets the type name for a constructor
   */
  private getTypeName(constructor: Function): string | undefined {
    for (const [name, type] of this.typeRegistry.entries()) {
      if (type === constructor) {
        return name;
      }
    }
    return undefined;
  }
}

/**
 * Type-safe JSON serializer with schema validation
 */
export class TypedJsonSerializer extends DefaultJsonSerializer {
  private schemas = new Map<string, object>();

  /**
   * Registers a JSON schema for a type
   */
  registerSchema(typeName: string, schema: object): void {
    this.schemas.set(typeName, schema);
  }

  serialize<T>(value: T): Result<string, SerializationError> {
    // Add runtime type checking if needed
    const result = super.serialize(value);
    
    if (result.isOk() && this.schemas.size > 0) {
      // Could validate against schema here
    }
    
    return result;
  }

  deserialize<T>(json: string, type?: new(...args: any[]) => T): Result<T, SerializationError> {
    const result = super.deserialize(json, type);
    
    if (result.isOk() && type) {
      // Could validate against schema here
      const typeName = type.name;
      const schema = this.schemas.get(typeName);
      
      if (schema) {
        // Perform schema validation
        // This is a placeholder - in real implementation, use a JSON schema validator
      }
    }
    
    return result;
  }
}

/**
 * Global default serializer instance
 */
export const defaultJsonSerializer = new DefaultJsonSerializer();