import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import request from 'supertest';
import type { Application } from 'express';
import { bootApp } from '../helpers/boot-app';
import { createMockDaprClient } from '../helpers/mock-dapr-client';
import { seedEvents } from '../helpers/seed-events';

describe('User Time-Travel Debugging', () => {
  let app: Application;
  let cleanup: () => Promise<void>;
  let mockDaprClient: ReturnType<typeof createMockDaprClient>;

  beforeEach(async () => {
    mockDaprClient = createMockDaprClient();
    const { app: testApp, cleanup: testCleanup } = await bootApp({
      daprClient: mockDaprClient.client
    });
    app = testApp;
    cleanup = testCleanup;
  });

  afterEach(async () => {
    await cleanup();
    mockDaprClient.reset();
  });

  describe('Historical State Reconstruction', () => {
    it('returns user state as it was at a specific point in time', async () => {
      // Arrange - Seed events directly to the event store
      const userId = 'user-42';
      await seedEvents(app, [
        {
          type: 'UserRegistered',
          aggregateId: userId,
          data: {
            userId: userId,
            name: 'Alice Johnson',
            email: 'alice@example.com',
            registeredAt: '2025-07-03T10:00:00.000Z'
          },
          version: 1,
          occurredAt: '2025-07-03T10:00:00.000Z'
        },
        {
          type: 'UserEmailUpdated',
          aggregateId: userId,
          data: {
            userId: userId,
            oldEmail: 'alice@example.com',
            newEmail: 'alice.new@example.com',
            updatedAt: '2025-07-03T11:00:00.000Z'
          },
          version: 2,
          occurredAt: '2025-07-03T11:00:00.000Z'
        },
        {
          type: 'UserNameUpdated',
          aggregateId: userId,
          data: {
            userId: userId,
            oldName: 'Alice Johnson',
            newName: 'Alice Smith',
            updatedAt: '2025-07-03T12:00:00.000Z'
          },
          version: 3,
          occurredAt: '2025-07-03T12:00:00.000Z'
        }
      ]);

      // Act - Request user state as it was before email update
      const response = await request(app)
        .get(`/users/${userId}`)
        .query({ asOf: '2025-07-03T10:30:00.000Z' })
        .expect(200);

      // Assert - Should return original state (only UserRegistered event)
      expect(response.body).toMatchObject({
        id: userId,
        name: 'Alice Johnson',
        email: 'alice@example.com',
        createdAt: '2025-07-03T10:00:00.000Z',
        // Should NOT include later updates
        _replayedAt: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/),
        _eventsReplayed: 1
      });
    });

    it('returns user state after email update but before name change', async () => {
      // Arrange - Same events as above
      const userId = 'user-43';
      await seedEvents(app, [
        {
          type: 'UserRegistered',
          aggregateId: userId,
          data: {
            userId: userId,
            name: 'Bob Wilson',
            email: 'bob@example.com',
            registeredAt: '2025-07-03T10:00:00.000Z'
          },
          version: 1,
          occurredAt: '2025-07-03T10:00:00.000Z'
        },
        {
          type: 'UserEmailUpdated',
          aggregateId: userId,
          data: {
            userId: userId,
            oldEmail: 'bob@example.com',
            newEmail: 'bob.new@example.com',
            updatedAt: '2025-07-03T11:00:00.000Z'
          },
          version: 2,
          occurredAt: '2025-07-03T11:00:00.000Z'
        },
        {
          type: 'UserNameUpdated',
          aggregateId: userId,
          data: {
            userId: userId,
            oldName: 'Bob Wilson',
            newName: 'Bob Smith',
            updatedAt: '2025-07-03T12:00:00.000Z'
          },
          version: 3,
          occurredAt: '2025-07-03T12:00:00.000Z'
        }
      ]);

      // Act - Request user state between email update and name change
      const response = await request(app)
        .get(`/users/${userId}`)
        .query({ asOf: '2025-07-03T11:30:00.000Z' })
        .expect(200);

      // Assert - Should include email update but not name change
      expect(response.body).toMatchObject({
        id: userId,
        name: 'Bob Wilson', // Original name
        email: 'bob.new@example.com', // Updated email
        createdAt: '2025-07-03T10:00:00.000Z',
        _replayedAt: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/),
        _eventsReplayed: 2 // UserRegistered + UserEmailUpdated
      });
    });

    it('returns current state when asOf is in the future', async () => {
      // Arrange
      const userId = 'user-44';
      await seedEvents(app, [
        {
          type: 'UserRegistered',
          aggregateId: userId,
          data: {
            userId: userId,
            name: 'Charlie Brown',
            email: 'charlie@example.com',
            registeredAt: '2025-07-03T10:00:00.000Z'
          },
          version: 1,
          occurredAt: '2025-07-03T10:00:00.000Z'
        }
      ]);

      // Act - Request state with future timestamp
      const response = await request(app)
        .get(`/users/${userId}`)
        .query({ asOf: '2025-07-04T00:00:00.000Z' })
        .expect(200);

      // Assert - Should return current state (all events)
      expect(response.body).toMatchObject({
        id: userId,
        name: 'Charlie Brown',
        email: 'charlie@example.com',
        _eventsReplayed: 1
      });
    });

    it('returns 404 when user did not exist at the specified time', async () => {
      // Arrange
      const userId = 'user-45';
      await seedEvents(app, [
        {
          type: 'UserRegistered',
          aggregateId: userId,
          data: {
            userId: userId,
            name: 'Diana Prince',
            email: 'diana@example.com',
            registeredAt: '2025-07-03T12:00:00.000Z'
          },
          version: 1,
          occurredAt: '2025-07-03T12:00:00.000Z'
        }
      ]);

      // Act - Request state before user was created
      await request(app)
        .get(`/users/${userId}`)
        .query({ asOf: '2025-07-03T10:00:00.000Z' })
        .expect(404);
    });

    it('returns current state when no asOf parameter is provided', async () => {
      // Arrange
      const userId = 'user-46';
      await seedEvents(app, [
        {
          type: 'UserRegistered',
          aggregateId: userId,
          data: {
            userId: userId,
            name: 'Eve Adams',
            email: 'eve@example.com',
            registeredAt: '2025-07-03T10:00:00.000Z'
          },
          version: 1,
          occurredAt: '2025-07-03T10:00:00.000Z'
        }
      ]);

      // Act - Normal GET request without asOf
      const response = await request(app)
        .get(`/users/${userId}`)
        .expect(200);

      // Assert - Should return current state without replay metadata
      expect(response.body).toMatchObject({
        id: userId,
        name: 'Eve Adams',
        email: 'eve@example.com'
      });

      // Should NOT have replay metadata for current state
      expect(response.body._replayedAt).toBeUndefined();
      expect(response.body._eventsReplayed).toBeUndefined();
    });
  });

  describe('Time-Travel Performance', () => {
    it('replays events efficiently for large event streams', async () => {
      // Arrange - Create a user with many updates
      const userId = 'user-performance-test';
      const events = [];
      
      // Initial registration
      events.push({
        type: 'UserRegistered',
        aggregateId: userId,
        data: {
          userId: userId,
          name: 'Performance Test User',
          email: 'perf@example.com',
          registeredAt: '2025-07-03T10:00:00.000Z'
        },
        version: 1,
        occurredAt: '2025-07-03T10:00:00.000Z'
      });

      // Add 50 email updates
      for (let i = 1; i <= 50; i++) {
        events.push({
          type: 'UserEmailUpdated',
          aggregateId: userId,
          data: {
            userId: userId,
            oldEmail: `perf${i-1}@example.com`,
            newEmail: `perf${i}@example.com`,
            updatedAt: `2025-07-03T${10 + Math.floor(i/10)}:${(i*2) % 60}:00.000Z`
          },
          version: i + 1,
          occurredAt: `2025-07-03T${10 + Math.floor(i/10)}:${(i*2) % 60}:00.000Z`
        });
      }

      await seedEvents(app, events);

      // Act - Replay to a point in the middle
      const startTime = Date.now();
      const response = await request(app)
        .get(`/users/${userId}`)
        .query({ asOf: '2025-07-03T11:30:00.000Z' })
        .expect(200);
      const duration = Date.now() - startTime;

      // Assert - Should be fast and return correct intermediate state
      expect(duration).toBeLessThan(100); // Should complete in under 100ms
      expect(response.body._eventsReplayed).toBeGreaterThan(1);
      expect(response.body._eventsReplayed).toBeLessThan(51); // Not all events
      expect(response.body.email).toMatch(/^perf\d+@example\.com$/);
    });
  });
});