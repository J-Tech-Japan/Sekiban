import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import request from 'supertest';
import type { Application } from 'express';
import { bootApp } from '../helpers/boot-app';

describe('User Sign-up - Acceptance Tests', () => {
  let app: Application;
  let cleanup: () => Promise<void>;

  beforeEach(async () => {
    const { app: testApp, cleanup: testCleanup } = await bootApp();
    app = testApp;
    cleanup = testCleanup;
  });

  afterEach(async () => {
    await cleanup();
  });

  it('POST /users creates a user and GET /users/:id returns it', async () => {
    // Arrange
    const userData = {
      name: 'Alice Johnson',
      email: 'alice@example.com'
    };

    // Act - Create user
    const createResponse = await request(app)
      .post('/users')
      .send(userData)
      .expect(201);

    // Assert - User created with ID
    expect(createResponse.body).toMatchObject({
      id: expect.any(String),
      success: true
    });

    const userId = createResponse.body.id;

    // Act - Get user
    const getResponse = await request(app)
      .get(`/users/${userId}`)
      .expect(200);

    // Assert - User data returned correctly
    expect(getResponse.body).toMatchObject({
      id: userId,
      name: 'Alice Johnson',
      email: 'alice@example.com',
      createdAt: expect.any(String)
    });
  });

  it('POST /users with invalid data returns 400', async () => {
    // Act & Assert - Invalid email
    await request(app)
      .post('/users')
      .send({ name: 'Alice', email: 'invalid-email' })
      .expect(400);

    // Act & Assert - Missing name
    await request(app)
      .post('/users')
      .send({ email: 'alice@example.com' })
      .expect(400);

    // Act & Assert - Missing email
    await request(app)
      .post('/users')
      .send({ name: 'Alice' })
      .expect(400);
  });

  it('GET /users/:id with non-existent user returns 404', async () => {
    // Act & Assert
    await request(app)
      .get('/users/non-existent-user-id')
      .expect(404);
  });

  it('POST /users with duplicate email returns 409', async () => {
    // Arrange
    const userData = {
      name: 'Alice Johnson',
      email: 'alice@example.com'
    };

    // Act - Create first user
    await request(app)
      .post('/users')
      .send(userData)
      .expect(201);

    // Act & Assert - Try to create duplicate
    await request(app)
      .post('/users')
      .send({ ...userData, name: 'Alice Duplicate' })
      .expect(409);
  });
});