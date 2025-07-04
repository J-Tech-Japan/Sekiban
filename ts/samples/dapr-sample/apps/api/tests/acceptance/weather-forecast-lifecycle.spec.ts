import { describe, it, expect, beforeEach } from 'vitest';
import request from 'supertest';
import type { Application } from 'express';
import { createApp } from '../../src/app.js';
import { DaprClient } from '@dapr/dapr';

describe('Weather Forecast Lifecycle', () => {
  let app: Application;
  let mockDaprClient: any;
  const forecastId = '123e4567-e89b-12d3-a456-426614174000';

  beforeEach(async () => {
    // Create mock Dapr client
    mockDaprClient = {
      actors: {
        getActor: vi.fn((actorType, actorId) => {
          // Track state of the aggregate
          let isDeleted = false;
          
          return {
            executeCommandAsync: vi.fn().mockImplementation(async (cmd) => {
              // Simulate state machine behavior
              if (cmd.command.commandType === 'DeleteWeatherForecast' || 
                  cmd.command.commandType === 'RemoveWeatherForecast') {
                isDeleted = true;
              }
              
              // Simulate failure for operations on deleted aggregates
              if (isDeleted && cmd.command.commandType === 'UpdateWeatherForecastLocation') {
                return {
                  aggregateId: forecastId,
                  lastSortableUniqueId: '20250704T120000Z.000001.123e4567',
                  success: false,
                  errorMessage: 'Weather forecast does not exist or has been deleted'
                };
              }
              
              return {
                aggregateId: forecastId,
                lastSortableUniqueId: '20250704T120000Z.000001.123e4567',
                success: true
              };
            })
          };
        })
      },
      pubsub: {
        publish: vi.fn().mockResolvedValue(undefined)
      }
    };

    // Create app with mocked dependencies
    app = await createApp({ daprClient: mockDaprClient });
  });

  describe('Update Location', () => {
    it('should update weather forecast location', async () => {
      // Act
      const response = await request(app)
        .post(`/api/weatherforecast/${forecastId}/update-location`)
        .send({ location: 'Osaka' })
        .expect('Content-Type', /json/);

      // Assert
      expect(response.status).toBe(200);
      expect(response.body).toMatchObject({
        success: true,
        aggregateId: forecastId,
        lastSortableUniqueId: expect.any(String)
      });
    });

    it('should validate location is required', async () => {
      // Act
      const response = await request(app)
        .post(`/api/weatherforecast/${forecastId}/update-location`)
        .send({});

      // Assert
      expect(response.status).toBe(400);
      expect(response.body).toMatchObject({
        success: false,
        error: expect.stringContaining('Missing required field')
      });
    });

    it('should validate aggregate ID format', async () => {
      // Act
      const response = await request(app)
        .post('/api/weatherforecast/invalid-id/update-location')
        .send({ location: 'Osaka' });

      // Assert
      expect(response.status).toBe(400);
      expect(response.body).toMatchObject({
        success: false,
        error: expect.stringContaining('Invalid command data')
      });
    });
  });

  describe('Delete vs Remove', () => {
    it('should soft delete weather forecast', async () => {
      // Act
      const response = await request(app)
        .post(`/api/weatherforecast/${forecastId}/delete`)
        .expect('Content-Type', /json/);

      // Assert
      expect(response.status).toBe(200);
      expect(response.body).toMatchObject({
        success: true,
        aggregateId: forecastId
      });
    });

    it('should remove weather forecast (hard delete)', async () => {
      // Act
      const response = await request(app)
        .post(`/api/weatherforecast/${forecastId}/remove`)
        .expect('Content-Type', /json/);

      // Assert
      expect(response.status).toBe(200);
      expect(response.body).toMatchObject({
        success: true,
        aggregateId: forecastId
      });
    });

    it('should not allow update after delete', async () => {
      // Arrange - delete first
      await request(app)
        .post(`/api/weatherforecast/${forecastId}/delete`);

      // Reset mock to simulate deleted state
      mockDaprClient.actors.getActor = vi.fn().mockReturnValue({
        executeCommandAsync: vi.fn().mockResolvedValue({
          aggregateId: forecastId,
          lastSortableUniqueId: '20250704T120000Z.000001.123e4567',
          success: false,
          errorMessage: 'Weather forecast does not exist or has been deleted'
        })
      });

      // Act - try to update
      const response = await request(app)
        .post(`/api/weatherforecast/${forecastId}/update-location`)
        .send({ location: 'Kyoto' });

      // Assert
      expect(response.status).toBe(400);
      expect(response.body).toMatchObject({
        success: false,
        error: expect.stringContaining('Weather forecast does not exist')
      });
    });
  });

  describe('Generate Sample Data', () => {
    it('should generate sample weather forecasts', async () => {
      // Act
      const response = await request(app)
        .post('/api/weatherforecast/generate')
        .expect('Content-Type', /json/);

      // Assert
      expect(response.status).toBe(200);
      expect(response.body).toMatchObject({
        success: true,
        message: 'Sample weather data generated',
        count: 15, // 5 cities × 3 days
        forecasts: expect.arrayContaining([
          expect.objectContaining({
            location: expect.any(String),
            date: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
            temperatureC: expect.any(Number),
            summary: expect.any(String),
            aggregateId: expect.any(String)
          })
        ])
      });
    });
  });
});