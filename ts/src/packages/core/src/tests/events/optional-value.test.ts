import { describe, it, expect } from 'vitest';
import { OptionalValue } from '../../events/event-retrieval-info';

describe('OptionalValue', () => {
  describe('empty()', () => {
    it('creates an optional value without a value', () => {
      const optional = OptionalValue.empty<string>();
      
      expect(optional.hasValueProperty).toBe(false);
    });

    it('throws when getting value from empty optional', () => {
      const optional = OptionalValue.empty<string>();
      
      expect(() => optional.getValue()).toThrow('OptionalValue has no value');
    });
  });

  describe('fromValue()', () => {
    it('creates an optional value with a string value', () => {
      const optional = OptionalValue.fromValue('test');
      
      expect(optional.hasValueProperty).toBe(true);
      expect(optional.getValue()).toBe('test');
    });

    it('creates an optional value with a number value', () => {
      const optional = OptionalValue.fromValue(42);
      
      expect(optional.hasValueProperty).toBe(true);
      expect(optional.getValue()).toBe(42);
    });

    it('creates an optional value with an object value', () => {
      const obj = { key: 'value' };
      const optional = OptionalValue.fromValue(obj);
      
      expect(optional.hasValueProperty).toBe(true);
      expect(optional.getValue()).toBe(obj);
    });
  });

  describe('fromNullableValue()', () => {
    it('creates empty optional from null', () => {
      const optional = OptionalValue.fromNullableValue<string>(null);
      
      expect(optional.hasValueProperty).toBe(false);
    });

    it('creates empty optional from undefined', () => {
      const optional = OptionalValue.fromNullableValue<string>(undefined);
      
      expect(optional.hasValueProperty).toBe(false);
    });

    it('creates optional with value from non-null value', () => {
      const optional = OptionalValue.fromNullableValue('test');
      
      expect(optional.hasValueProperty).toBe(true);
      expect(optional.getValue()).toBe('test');
    });

    it('creates optional with value from zero', () => {
      const optional = OptionalValue.fromNullableValue(0);
      
      expect(optional.hasValueProperty).toBe(true);
      expect(optional.getValue()).toBe(0);
    });

    it('creates optional with value from empty string', () => {
      const optional = OptionalValue.fromNullableValue('');
      
      expect(optional.hasValueProperty).toBe(true);
      expect(optional.getValue()).toBe('');
    });
  });
});