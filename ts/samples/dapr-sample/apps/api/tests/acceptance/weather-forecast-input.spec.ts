import { describe, it, expect, beforeEach } from 'vitest';
import request from 'supertest';
import type { Application } from 'express';
import { createApp } from '../../src/app.js';
import { DaprClient } from '@dapr/dapr';

describe('Weather Forecast Input', () => {
  let app: Application;
  let mockDaprClient: any;

  beforeEach(async () => {
    // Create mock Dapr client
    mockDaprClient = {
      actors: {
        getActor: vi.fn().mockReturnValue({
          executeCommandAsync: vi.fn().mockResolvedValue({
            aggregateId: '123e4567-e89b-12d3-a456-426614174000',
            lastSortableUniqueId: '20250704T120000Z.000001.123e4567',
            success: true
          })
        })
      },
      pubsub: {
        publish: vi.fn().mockResolvedValue(undefined)
      }
    };

    // Create app with mocked dependencies
    app = await createApp({ daprClient: mockDaprClient });
  });

  describe('POST /api/weatherforecast/input', () => {
    it('should create a new weather forecast', async () => {
      // Arrange
      const newForecast = {
        location: 'Tokyo',
        date: '2025-07-04',
        temperatureC: 25,
        summary: 'Warm and sunny'
      };

      // Act
      const response = await request(app)
        .post('/api/weatherforecast/input')
        .send(newForecast)
        .expect('Content-Type', /json/);

      // Assert
      expect(response.status).toBe(200);
      expect(response.body).toMatchObject({
        success: true,
        aggregateId: expect.stringMatching(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/),
        lastSortableUniqueId: expect.any(String)
      });

      // Verify actor was called
      expect(mockDaprClient.actors.getActor).toHaveBeenCalled();
    });

    it('should validate required fields', async () => {
      // Arrange - missing location
      const invalidForecast = {
        date: '2025-07-04',
        temperatureC: 25
      };

      // Act
      const response = await request(app)
        .post('/api/weatherforecast/input')
        .send(invalidForecast);

      // Assert
      expect(response.status).toBe(400);
      expect(response.body).toMatchObject({
        success: false,
        error: expect.stringContaining('Missing required fields')
      });
    });

    it('should validate temperature range', async () => {
      // Arrange - temperature too low
      const invalidForecast = {
        location: 'Antarctica',
        date: '2025-07-04',
        temperatureC: -300, // Below absolute zero
        summary: 'Impossible cold'
      };

      // Act
      const response = await request(app)
        .post('/api/weatherforecast/input')
        .send(invalidForecast);

      // Assert
      expect(response.status).toBe(400);
      expect(response.body).toMatchObject({
        success: false,
        error: expect.stringContaining('Invalid command data')
      });
    });

    it('should validate date format', async () => {
      // Arrange - invalid date format
      const invalidForecast = {
        location: 'Tokyo',
        date: '04/07/2025', // Wrong format
        temperatureC: 25,
        summary: 'Warm'
      };

      // Act
      const response = await request(app)
        .post('/api/weatherforecast/input')
        .send(invalidForecast);

      // Assert
      expect(response.status).toBe(400);
      expect(response.body).toMatchObject({
        success: false,
        error: expect.stringContaining('Invalid command data')
      });
    });

    it('should handle optional summary field', async () => {
      // Arrange - no summary
      const forecastWithoutSummary = {
        location: 'Tokyo',
        date: '2025-07-04',
        temperatureC: 25
      };

      // Act
      const response = await request(app)
        .post('/api/weatherforecast/input')
        .send(forecastWithoutSummary);

      // Assert
      expect(response.status).toBe(200);
      expect(response.body).toMatchObject({
        success: true,
        aggregateId: expect.any(String)
      });
    });

    it('should handle actor communication errors', async () => {
      // Arrange
      mockDaprClient.actors.getActor = vi.fn().mockReturnValue({
        executeCommandAsync: vi.fn().mockRejectedValue(new Error('Actor unavailable'))
      });

      const newForecast = {
        location: 'Tokyo',
        date: '2025-07-04',
        temperatureC: 25,
        summary: 'Warm'
      };

      // Act
      const response = await request(app)
        .post('/api/weatherforecast/input')
        .send(newForecast);

      // Assert
      expect(response.status).toBe(400);
      expect(response.body).toMatchObject({
        success: false,
        error: expect.stringContaining('Actor unavailable')
      });
    });
  });
});