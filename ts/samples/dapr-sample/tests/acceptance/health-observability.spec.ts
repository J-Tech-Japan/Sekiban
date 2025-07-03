import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import request from 'supertest';
import type { Application } from 'express';
import { bootApp } from '../helpers/boot-app';
import { createMockDaprClient } from '../helpers/mock-dapr-client';

describe('Service Health & Observability', () => {
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

  describe('Liveness Endpoint', () => {
    it('exposes a liveness endpoint at /healthz', async () => {
      // Act & Assert
      const response = await request(app)
        .get('/healthz')
        .expect(200);

      expect(response.body).toEqual({ 
        status: 'ok',
        timestamp: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/)
      });
    });

    it('returns liveness even when dependencies are down', async () => {
      // Arrange - Simulate dependency failure
      mockDaprClient.simulatePublishFailure(new Error('Dapr sidecar unavailable'));

      // Act & Assert - Liveness should still return ok (container is alive)
      await request(app)
        .get('/healthz')
        .expect(200);
    });
  });

  describe('Readiness Endpoint', () => {
    it('exposes a readiness endpoint at /readyz', async () => {
      // Act & Assert
      const response = await request(app)
        .get('/readyz')
        .expect(200);

      expect(response.body).toEqual({
        status: 'ready',
        checks: {
          database: 'ok',
          dapr: 'ok',
          eventStore: 'ok'
        },
        timestamp: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/)
      });
    });

    it('returns 503 when Dapr sidecar is unavailable', async () => {
      // Arrange - Simulate Dapr failure
      mockDaprClient.simulatePublishFailure(new Error('Dapr sidecar unavailable'));

      // Act & Assert
      const response = await request(app)
        .get('/readyz')
        .expect(503);

      expect(response.body).toEqual({
        status: 'not_ready',
        checks: {
          database: 'ok',
          dapr: 'error',
          eventStore: 'ok'
        },
        errors: ['Dapr sidecar unavailable'],
        timestamp: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/)
      });
    });
  });

  describe('Metrics Endpoint', () => {
    it('exposes Prometheus metrics at /metrics', async () => {
      // Act
      const response = await request(app)
        .get('/metrics')
        .expect(200);

      // Assert - Prometheus format
      expect(response.headers['content-type']).toMatch(/text\/plain/);
      expect(response.text).toMatch(/^# HELP /m); // Prometheus comment format
      expect(response.text).toMatch(/^# TYPE /m); // Prometheus type definitions
    });

    it('increments user_registered_total counter when user is created', async () => {
      // Arrange - Get baseline metrics
      const baselineResponse = await request(app).get('/metrics');
      const baselineCount = extractMetricValue(baselineResponse.text, 'user_registered_total');

      // Act - Create a user
      await request(app)
        .post('/users')
        .send({ name: 'Taro Yamada', email: 'taro@example.com' })
        .expect(201);

      // Assert - Metric incremented
      const metricsResponse = await request(app).get('/metrics');
      const currentCount = extractMetricValue(metricsResponse.text, 'user_registered_total');
      
      expect(currentCount).toBe(baselineCount + 1);
      expect(metricsResponse.text).toMatch(/user_registered_total{.*} \d+/);
    });

    it('tracks HTTP request duration histogram', async () => {
      // Act - Make a request
      await request(app)
        .get('/health')
        .expect(200);

      // Assert - HTTP duration metric exists
      const response = await request(app).get('/metrics');
      expect(response.text).toMatch(/http_request_duration_seconds_bucket/);
      expect(response.text).toMatch(/http_request_duration_seconds_count/);
      expect(response.text).toMatch(/http_request_duration_seconds_sum/);
    });

    it('does not increment user_registered_total when user creation fails', async () => {
      // Arrange - Get baseline metrics
      const baselineResponse = await request(app).get('/metrics');
      const baselineCount = extractMetricValue(baselineResponse.text, 'user_registered_total');

      // Act - Try to create invalid user
      await request(app)
        .post('/users')
        .send({ name: '', email: 'invalid-email' }) // Invalid data
        .expect(400);

      // Assert - Metric not incremented
      const metricsResponse = await request(app).get('/metrics');
      const currentCount = extractMetricValue(metricsResponse.text, 'user_registered_total');
      
      expect(currentCount).toBe(baselineCount); // No change
    });
  });

  describe('Tracing Integration', () => {
    it('includes trace context in successful user creation', async () => {
      // Act - Create user with trace headers
      const response = await request(app)
        .post('/users')
        .set('traceparent', '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01')
        .send({ name: 'Hanako Sato', email: 'hanako@example.com' })
        .expect(201);

      // Assert - Response includes trace context
      expect(response.headers['traceparent']).toBeDefined();
      
      // Verify event was published with trace context
      const publishedMessages = mockDaprClient.getPublishedMessages();
      expect(publishedMessages).toHaveLength(1);
      
      const event = publishedMessages[0];
      expect(event.cloudEvent.data).toMatchObject({
        userId: expect.any(String),
        name: 'Hanako Sato',
        email: 'hanako@example.com'
      });
    });
  });
});

// Helper function to extract metric values from Prometheus format
function extractMetricValue(metricsText: string, metricName: string): number {
  const regex = new RegExp(`${metricName}(?:{[^}]*})? (\\d+(?:\\.\\d+)?)`);
  const match = metricsText.match(regex);
  return match ? parseFloat(match[1]) : 0; // Default to 0 if metric doesn't exist
}