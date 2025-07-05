import type { ISekibanSerializer } from '../interfaces.js';

export class DefaultSekibanSerializer implements ISekibanSerializer {
  serialize(value: any): string {
    return JSON.stringify(value, (key, val) => {
      // Handle Date objects
      if (val instanceof Date) {
        return { $type: 'Date', value: val.toISOString() };
      }
      // Handle BigInt
      if (typeof val === 'bigint') {
        return { $type: 'BigInt', value: val.toString() };
      }
      // Handle undefined (JSON.stringify normally omits undefined)
      if (val === undefined) {
        return { $type: 'undefined' };
      }
      // Handle Map
      if (val instanceof Map) {
        return { $type: 'Map', value: Array.from(val.entries()) };
      }
      // Handle Set
      if (val instanceof Set) {
        return { $type: 'Set', value: Array.from(val) };
      }
      return val;
    });
  }

  deserialize<T>(json: string, type?: new (...args: any[]) => T): T {
    const parsed = JSON.parse(json, (key, val) => {
      if (val && typeof val === 'object' && '$type' in val) {
        switch (val.$type) {
          case 'Date':
            return new Date(val.value);
          case 'BigInt':
            return BigInt(val.value);
          case 'undefined':
            return undefined;
          case 'Map':
            return new Map(val.value);
          case 'Set':
            return new Set(val.value);
        }
      }
      return val;
    });

    // If a type constructor is provided, create an instance
    if (type) {
      return Object.assign(new type(), parsed);
    }

    return parsed;
  }
}