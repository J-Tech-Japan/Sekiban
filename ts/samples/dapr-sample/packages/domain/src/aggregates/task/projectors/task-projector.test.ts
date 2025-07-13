import { describe, it, expect } from 'vitest';
import { taskProjector } from './task-projector.js';
import { TaskCreated, TaskAssigned, TaskCompleted } from '../events/task-events.js';
import { PartitionKeys } from '@sekiban/core';

describe('TaskProjector', () => {
  it('should project TaskCreated event', () => {
    const initialState = taskProjector.initialState();
    const event = TaskCreated.create({
      taskId: '123',
      title: 'Test Task',
      description: 'Test Description',
      assignedTo: 'test@example.com',
      priority: 'high',
      createdAt: '2024-01-01T00:00:00Z'
    });

    const newState = taskProjector.projections.TaskCreated(initialState, event);

    expect(newState.aggregateType).toBe('Task');
    if (newState.aggregateType === 'Task') {
      expect(newState.taskId).toBe('123');
      expect(newState.title).toBe('Test Task');
      expect(newState.description).toBe('Test Description');
      expect(newState.assignedTo).toBe('test@example.com');
      expect(newState.priority).toBe('high');
      expect(newState.status).toBe('active');
    }
  });

  it('should project TaskAssigned event', () => {
    const currentState = {
      aggregateType: 'Task' as const,
      taskId: '123',
      title: 'Test Task',
      status: 'active' as const,
      priority: 'medium' as const,
      createdAt: '2024-01-01T00:00:00Z',
      updatedAt: '2024-01-01T00:00:00Z'
    };

    const event = TaskAssigned.create({
      taskId: '123',
      assignedTo: 'new@example.com',
      assignedAt: '2024-01-02T00:00:00Z'
    });

    const newState = taskProjector.projections.TaskAssigned(currentState, event);

    expect(newState.aggregateType).toBe('Task');
    if (newState.aggregateType === 'Task') {
      expect(newState.assignedTo).toBe('new@example.com');
      expect(newState.updatedAt).toBe('2024-01-02T00:00:00Z');
    }
  });

  it('should project TaskCompleted event', () => {
    const currentState = {
      aggregateType: 'Task' as const,
      taskId: '123',
      title: 'Test Task',
      status: 'active' as const,
      priority: 'medium' as const,
      createdAt: '2024-01-01T00:00:00Z',
      updatedAt: '2024-01-01T00:00:00Z'
    };

    const event = TaskCompleted.create({
      taskId: '123',
      completedBy: 'user@example.com',
      completedAt: '2024-01-03T00:00:00Z',
      notes: 'Done!'
    });

    const newState = taskProjector.projections.TaskCompleted(currentState, event);

    expect(newState.aggregateType).toBe('Task');
    if (newState.aggregateType === 'Task') {
      expect(newState.status).toBe('completed');
      expect(newState.completedAt).toBe('2024-01-03T00:00:00Z');
      expect(newState.updatedAt).toBe('2024-01-03T00:00:00Z');
    }
  });
});