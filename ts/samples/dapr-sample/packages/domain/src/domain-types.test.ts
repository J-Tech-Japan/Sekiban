import { describe, it, expect } from 'vitest';
import { createTaskDomainTypes } from './domain-types.js';
import { globalRegistry } from '@sekiban/core';

describe('Domain Types Registration', () => {
  const domainTypes = createTaskDomainTypes();

  describe('Commands Registration', () => {
    it('should have all commands registered', () => {
      const expectedCommands = [
        'CreateTask',
        'AssignTask',
        'CompleteTask',
        'UpdateTask',
        'DeleteTask',
        'RevertTaskCompletion'
      ];

      expectedCommands.forEach(commandName => {
        expect(domainTypes.commands.has(commandName)).toBe(true);
      });
    });

    it('should find commands by name', () => {
      const createTaskCommand = domainTypes.commands.get('CreateTask');
      expect(createTaskCommand).toBeDefined();
      expect(createTaskCommand?.name).toBe('CreateTask');

      const assignTaskCommand = domainTypes.commands.get('AssignTask');
      expect(assignTaskCommand).toBeDefined();
      expect(assignTaskCommand?.name).toBe('AssignTask');
    });

    it('should have correct total number of commands', () => {
      expect(domainTypes.commands.size).toBe(6);
    });
  });

  describe('Events Registration', () => {
    it('should have all events registered', () => {
      const expectedEvents = [
        'TaskCreated',
        'TaskAssigned',
        'TaskCompleted',
        'TaskUpdated',
        'TaskDeleted',
        'TaskCompletionReverted'
      ];

      expectedEvents.forEach(eventName => {
        expect(domainTypes.events.has(eventName)).toBe(true);
      });
    });

    it('should find events by name', () => {
      const taskCreatedEvent = domainTypes.events.get('TaskCreated');
      expect(taskCreatedEvent).toBeDefined();
      expect(taskCreatedEvent?.name).toBe('TaskCreated');

      const taskCompletedEvent = domainTypes.events.get('TaskCompleted');
      expect(taskCompletedEvent).toBeDefined();
      expect(taskCompletedEvent?.name).toBe('TaskCompleted');
    });

    it('should have correct total number of events', () => {
      expect(domainTypes.events.size).toBe(6);
    });
  });

  describe('Projectors Registration', () => {
    it('should have task projector registered', () => {
      expect(domainTypes.projectors.has('TaskProjector')).toBe(true);
    });

    it('should find projector by name', () => {
      const taskProjector = domainTypes.projectors.get('TaskProjector');
      expect(taskProjector).toBeDefined();
      expect(taskProjector?.name).toBe('TaskProjector');
    });

    it('should have correct total number of projectors', () => {
      expect(domainTypes.projectors.size).toBe(1);
    });
  });

  describe('Registry Lookup Functions', () => {
    it('should find command definition by name', () => {
      const createTaskDef = domainTypes.findCommandDefinition('CreateTask');
      expect(createTaskDef).toBeDefined();
      expect(createTaskDef?.name).toBe('CreateTask');
    });

    it('should find event definition by name', () => {
      const taskCreatedDef = domainTypes.findEventDefinition('TaskCreated');
      expect(taskCreatedDef).toBeDefined();
      expect(taskCreatedDef?.name).toBe('TaskCreated');
    });

    it('should find projector definition by name', () => {
      const taskProjectorDef = domainTypes.findProjectorDefinition('TaskProjector');
      expect(taskProjectorDef).toBeDefined();
      expect(taskProjectorDef?.name).toBe('TaskProjector');
    });

    it('should return undefined for non-existent definitions', () => {
      expect(domainTypes.findCommandDefinition('NonExistentCommand')).toBeUndefined();
      expect(domainTypes.findEventDefinition('NonExistentEvent')).toBeUndefined();
      expect(domainTypes.findProjectorDefinition('NonExistentProjector')).toBeUndefined();
    });
  });

  describe('Global Registry State', () => {
    it('should have all types registered in global registry', () => {
      // Commands
      expect(globalRegistry.commands.has('CreateTask')).toBe(true);
      expect(globalRegistry.commands.has('AssignTask')).toBe(true);
      expect(globalRegistry.commands.has('CompleteTask')).toBe(true);
      expect(globalRegistry.commands.has('UpdateTask')).toBe(true);
      expect(globalRegistry.commands.has('DeleteTask')).toBe(true);
      expect(globalRegistry.commands.has('RevertTaskCompletion')).toBe(true);

      // Events
      expect(globalRegistry.events.has('TaskCreated')).toBe(true);
      expect(globalRegistry.events.has('TaskAssigned')).toBe(true);
      expect(globalRegistry.events.has('TaskCompleted')).toBe(true);
      expect(globalRegistry.events.has('TaskUpdated')).toBe(true);
      expect(globalRegistry.events.has('TaskDeleted')).toBe(true);
      expect(globalRegistry.events.has('TaskCompletionReverted')).toBe(true);

      // Projectors
      expect(globalRegistry.projectors.has('TaskProjector')).toBe(true);
    });
  });
});