import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import request from 'supertest';
import type { Application } from 'express';
import { bootApp } from '../helpers/boot-app';
import { createMockDaprClient } from '../helpers/mock-dapr-client';

describe('User Registration → Pub/Sub Integration', () => {
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

  it('publishes CloudEvent to "users" topic when user is registered', async () => {
    // Arrange
    const userData = {
      name: 'Alice Johnson',
      email: 'alice@example.com'
    };

    // Act - Create user
    const response = await request(app)
      .post('/users')
      .send(userData)
      .expect(201);

    // Assert - User created
    expect(response.body).toMatchObject({
      id: expect.any(String),
      success: true
    });

    const userId = response.body.id;

    // Assert - CloudEvent published to users topic
    expect(mockDaprClient.getPublishedMessages()).toHaveLength(1);
    
    const publishedMessage = mockDaprClient.getPublishedMessages()[0];
    expect(publishedMessage).toMatchObject({
      topic: 'users',
      eventType: 'UserRegistered',
      cloudEvent: {
        id: expect.any(String),
        type: 'UserRegistered',
        source: '/users',
        specversion: '1.0',
        time: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/),
        data: {
          userId: userId,
          name: 'Alice Johnson',
          email: 'alice@example.com',
          registeredAt: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/)
        }
      }
    });
  });

  it('publishes CloudEvent with correct metadata and headers', async () => {
    // Arrange
    const userData = {
      name: 'Bob Smith',
      email: 'bob@example.com'
    };

    // Act
    await request(app)
      .post('/users')
      .send(userData)
      .expect(201);

    // Assert - CloudEvent metadata
    const publishedMessage = mockDaprClient.getPublishedMessages()[0];
    expect(publishedMessage.cloudEvent).toMatchObject({
      specversion: '1.0',
      type: 'UserRegistered',
      source: '/users',
      datacontenttype: 'application/json'
    });

    // Assert - Required CloudEvent fields are present
    expect(publishedMessage.cloudEvent.id).toBeDefined();
    expect(publishedMessage.cloudEvent.time).toBeDefined();
    expect(publishedMessage.cloudEvent.data).toBeDefined();
  });

  it('does not publish event when user registration fails', async () => {
    // Arrange - Invalid user data
    const invalidUserData = {
      name: '', // Invalid: empty name
      email: 'invalid-email' // Invalid: malformed email
    };

    // Act
    await request(app)
      .post('/users')
      .send(invalidUserData)
      .expect(400);

    // Assert - No events published
    expect(mockDaprClient.getPublishedMessages()).toHaveLength(0);
  });

  it('handles pub/sub failures gracefully without affecting user creation', async () => {
    // Arrange
    mockDaprClient.simulatePublishFailure(new Error('Pub/sub service unavailable'));
    
    const userData = {
      name: 'Charlie Brown',
      email: 'charlie@example.com'
    };

    // Act - User should still be created even if pub/sub fails
    const response = await request(app)
      .post('/users')
      .send(userData)
      .expect(201);

    // Assert - User created successfully
    expect(response.body).toMatchObject({
      id: expect.any(String),
      success: true
    });

    // Assert - User can still be retrieved
    const userId = response.body.id;
    await request(app)
      .get(`/users/${userId}`)
      .expect(200);

    // Assert - Publish was attempted but failed
    expect(mockDaprClient.getPublishErrors()).toHaveLength(1);
  });
});