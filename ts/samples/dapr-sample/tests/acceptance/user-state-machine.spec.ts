import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import request from 'supertest';
import type { Application } from 'express';
import { bootApp } from '../helpers/boot-app';
import { createMockDaprClient } from '../helpers/mock-dapr-client';

describe('User State Machine - Acceptance Tests', () => {
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

  describe('UnconfirmedUser → ConfirmedUser State Transition', () => {
    it('should create user in UnconfirmedUser state and allow confirmation to ConfirmedUser', async () => {
      // Arrange & Act - Create user (should be in UnconfirmedUser state)
      const createResponse = await request(app)
        .post('/api/users/create')
        .send({ name: 'Alice Johnson', email: 'alice@example.com' })
        .expect(201);

      const userId = createResponse.body.id;
      expect(userId).toBeDefined();

      // Act - Get user in initial state
      const unconfirmedResponse = await request(app)
        .get(`/api/users/${userId}`)
        .expect(200);

      // Assert - Should be UnconfirmedUser
      expect(unconfirmedResponse.body).toMatchObject({
        aggregateType: 'UnconfirmedUser',
        id: userId,
        name: 'Alice Johnson',
        email: 'alice@example.com',
        createdAt: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/)
      });
      expect(unconfirmedResponse.body.confirmedAt).toBeUndefined();

      // Act - Confirm user
      const confirmResponse = await request(app)
        .post('/api/users/confirm')
        .send({ userId })
        .expect(200);

      expect(confirmResponse.body.success).toBe(true);

      // Act - Get user after confirmation
      const confirmedResponse = await request(app)
        .get(`/api/users/${userId}`)
        .expect(200);

      // Assert - Should be ConfirmedUser with confirmedAt timestamp
      expect(confirmedResponse.body).toMatchObject({
        aggregateType: 'ConfirmedUser',
        id: userId,
        name: 'Alice Johnson',
        email: 'alice@example.com',
        createdAt: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/),
        confirmedAt: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/)
      });
    });

    it('should prevent confirming already confirmed user', async () => {
      // Arrange - Create and confirm user
      const createResponse = await request(app)
        .post('/api/users/create')
        .send({ name: 'Bob Wilson', email: 'bob@example.com' })
        .expect(201);

      const userId = createResponse.body.id;

      await request(app)
        .post('/api/users/confirm')
        .send({ userId })
        .expect(200);

      // Act - Try to confirm already confirmed user
      const doubleConfirmResponse = await request(app)
        .post('/api/users/confirm')
        .send({ userId })
        .expect(400);

      // Assert - Should get error
      expect(doubleConfirmResponse.body).toMatchObject({
        success: false,
        error: expect.stringContaining('UnconfirmedUser')
      });
    });

    it('should prevent confirming non-existent user', async () => {
      // Act - Try to confirm non-existent user
      const response = await request(app)
        .post('/api/users/confirm')
        .send({ userId: 'non-existent-user-id' })
        .expect(404);

      // Assert
      expect(response.body).toMatchObject({
        success: false,
        error: expect.stringContaining('not found')
      });
    });
  });

  describe('Name Updates Across States', () => {
    it('should allow name updates for UnconfirmedUser and maintain state', async () => {
      // Arrange - Create user
      const createResponse = await request(app)
        .post('/api/users/create')
        .send({ name: 'Charlie Brown', email: 'charlie@example.com' })
        .expect(201);

      const userId = createResponse.body.id;

      // Act - Update name while unconfirmed
      await request(app)
        .post('/api/users/update-name')
        .send({ userId, newName: 'Charlie Smith' })
        .expect(200);

      // Assert - Should still be UnconfirmedUser with updated name
      const response = await request(app)
        .get(`/api/users/${userId}`)
        .expect(200);

      expect(response.body).toMatchObject({
        aggregateType: 'UnconfirmedUser',
        id: userId,
        name: 'Charlie Smith',
        email: 'charlie@example.com'
      });
    });

    it('should allow name updates for ConfirmedUser and maintain state', async () => {
      // Arrange - Create and confirm user
      const createResponse = await request(app)
        .post('/api/users/create')
        .send({ name: 'Diana Prince', email: 'diana@example.com' })
        .expect(201);

      const userId = createResponse.body.id;

      await request(app)
        .post('/api/users/confirm')
        .send({ userId })
        .expect(200);

      // Act - Update name while confirmed
      await request(app)
        .post('/api/users/update-name')
        .send({ userId, newName: 'Diana Wonder' })
        .expect(200);

      // Assert - Should still be ConfirmedUser with updated name
      const response = await request(app)
        .get(`/api/users/${userId}`)
        .expect(200);

      expect(response.body).toMatchObject({
        aggregateType: 'ConfirmedUser',
        id: userId,
        name: 'Diana Wonder',
        email: 'diana@example.com',
        confirmedAt: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/)
      });
    });
  });

  describe('Event Publishing', () => {
    it('should publish UserCreated and UserConfirmed events', async () => {
      // Arrange - Create user
      const createResponse = await request(app)
        .post('/api/users/create')
        .send({ name: 'Eve Adams', email: 'eve@example.com' })
        .expect(201);

      const userId = createResponse.body.id;

      // Act - Confirm user
      await request(app)
        .post('/api/users/confirm')
        .send({ userId })
        .expect(200);

      // Assert - Should have published both events
      const publishedMessages = mockDaprClient.getPublishedMessages();
      expect(publishedMessages).toHaveLength(2);

      // Check UserCreated event
      expect(publishedMessages[0]).toMatchObject({
        topic: 'domain-events',
        eventType: 'UserCreated',
        cloudEvent: {
          data: {
            userId,
            name: 'Eve Adams',
            email: 'eve@example.com'
          }
        }
      });

      // Check UserConfirmed event
      expect(publishedMessages[1]).toMatchObject({
        topic: 'domain-events',
        eventType: 'UserConfirmed',
        cloudEvent: {
          data: {
            userId,
            confirmedAt: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/)
          }
        }
      });
    });
  });

  describe('Validation', () => {
    it('should validate CreateUser command data', async () => {
      // Act & Assert - Invalid name
      await request(app)
        .post('/api/users/create')
        .send({ name: '', email: 'test@example.com' })
        .expect(400);

      // Act & Assert - Invalid email
      await request(app)
        .post('/api/users/create')
        .send({ name: 'Test User', email: 'invalid-email' })
        .expect(400);
    });

    it('should validate ConfirmUser command data', async () => {
      // Act & Assert - Invalid userId
      await request(app)
        .post('/api/users/confirm')
        .send({ userId: 'invalid-uuid' })
        .expect(400);

      // Act & Assert - Missing userId
      await request(app)
        .post('/api/users/confirm')
        .send({})
        .expect(400);
    });
  });
});