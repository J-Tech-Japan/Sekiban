import { describe, it, expect } from 'vitest';
import { CommandBuilder } from './command-builder';
import { ICommand, PartitionKeys, Metadata } from '../../../core/src';

// Test commands
interface CreateUser extends ICommand {
  name: string;
  email: string;
}

interface UpdateUser extends ICommand {
  userId: string;
  name?: string;
  email?: string;
}

describe('CommandBuilder', () => {
  describe('Basic Command Building', () => {
    it('should build a simple command', () => {
      const builder = new CommandBuilder<CreateUser>('CreateUser');
      
      const command = builder
        .withPayload({
          name: 'John Doe',
          email: 'john@example.com'
        })
        .build();
      
      expect(command.type).toBe('CreateUser');
      expect(command.payload.name).toBe('John Doe');
      expect(command.payload.email).toBe('john@example.com');
    });

    it('should build command with partition keys', () => {
      const partitionKeys = PartitionKeys.create('user-123', 'users');
      const builder = new CommandBuilder<CreateUser>('CreateUser');
      
      const command = builder
        .withPayload({
          name: 'John Doe',
          email: 'john@example.com'
        })
        .withPartitionKeys(partitionKeys)
        .build();
      
      expect(command.partitionKeys).toBe(partitionKeys);
    });

    it('should build command with metadata', () => {
      const metadata: Metadata = {
        correlationId: 'corr-123',
        userId: 'admin-456'
      };
      
      const command = new CommandBuilder<CreateUser>('CreateUser')
        .withPayload({
          name: 'John Doe',
          email: 'john@example.com'
        })
        .withMetadata(metadata)
        .build();
      
      expect(command.metadata).toEqual(metadata);
    });
  });

  describe('Fluent API', () => {
    it('should support method chaining', () => {
      const partitionKeys = PartitionKeys.create('user-123', 'users');
      
      const command = new CommandBuilder<CreateUser>('CreateUser')
        .withPayload({ name: 'John', email: 'john@example.com' })
        .withPartitionKeys(partitionKeys)
        .withMetadata({ source: 'test' })
        .build();
      
      expect(command.type).toBe('CreateUser');
      expect(command.partitionKeys).toBe(partitionKeys);
      expect(command.metadata?.source).toBe('test');
    });
  });

  describe('Partial Updates', () => {
    it('should support updating payload partially', () => {
      const builder = new CommandBuilder<UpdateUser>('UpdateUser');
      
      const command = builder
        .withPayload({ userId: 'user-123' })
        .updatePayload({ name: 'Jane Doe' })
        .updatePayload({ email: 'jane@example.com' })
        .build();
      
      expect(command.payload.userId).toBe('user-123');
      expect(command.payload.name).toBe('Jane Doe');
      expect(command.payload.email).toBe('jane@example.com');
    });
  });

  describe('Copy and Modify', () => {
    it('should create a copy of existing command with modifications', () => {
      const original = new CommandBuilder<CreateUser>('CreateUser')
        .withPayload({ name: 'John', email: 'john@example.com' })
        .withMetadata({ source: 'original' })
        .build();
      
      const modified = CommandBuilder.from(original)
        .updatePayload({ name: 'John Doe' })
        .withMetadata({ source: 'modified' })
        .build();
      
      expect(modified.type).toBe(original.type);
      expect(modified.payload.email).toBe(original.payload.email);
      expect(modified.payload.name).toBe('John Doe');
      expect(modified.metadata?.source).toBe('modified');
    });
  });

  describe('Validation', () => {
    it('should throw error when building without payload', () => {
      const builder = new CommandBuilder<CreateUser>('CreateUser');
      
      expect(() => builder.build()).toThrow('Payload is required');
    });
  });

  describe('Command Variants', () => {
    it('should create multiple command variants', () => {
      const builder = new CommandBuilder<CreateUser>('CreateUser');
      
      const commands = builder.buildMany([
        { name: 'User 1', email: 'user1@example.com' },
        { name: 'User 2', email: 'user2@example.com' },
        { name: 'User 3', email: 'user3@example.com' }
      ]);
      
      expect(commands).toHaveLength(3);
      expect(commands[0].payload.name).toBe('User 1');
      expect(commands[1].payload.name).toBe('User 2');
      expect(commands[2].payload.name).toBe('User 3');
      
      // All should have the same type
      commands.forEach(cmd => {
        expect(cmd.type).toBe('CreateUser');
      });
    });

    it('should apply common metadata to all variants', () => {
      const metadata = { source: 'batch-import' };
      const partitionKeys = PartitionKeys.create('batch-123', 'users');
      
      const commands = new CommandBuilder<CreateUser>('CreateUser')
        .withMetadata(metadata)
        .withPartitionKeys(partitionKeys)
        .buildMany([
          { name: 'User 1', email: 'user1@example.com' },
          { name: 'User 2', email: 'user2@example.com' }
        ]);
      
      commands.forEach(cmd => {
        expect(cmd.metadata).toEqual(metadata);
        expect(cmd.partitionKeys).toBe(partitionKeys);
      });
    });
  });
});