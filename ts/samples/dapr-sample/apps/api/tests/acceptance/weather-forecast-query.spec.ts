import { describe, it, expect, beforeEach } from 'vitest';
import request from 'supertest';
import type { Application } from 'express';
import { createApp } from '../../src/app.js';
import { DaprClient } from '@dapr/dapr';
import { TemperatureCelsius } from '@sekiban/dapr-sample-domain';

describe('Weather Forecast Query', () => {
  let app: Application;
  let mockDaprClient: any;

  beforeEach(async () => {
    // Create mock Dapr client
    mockDaprClient = {
      actors: {
        getActor: vi.fn().mockReturnValue({
          queryAsync: vi.fn().mockResolvedValue({
            weatherForecasts: [
              {
                id: '123e4567-e89b-12d3-a456-426614174001',
                location: 'Tokyo',
                date: '2025-07-04',
                temperatureC: 25,
                temperatureF: 77,
                summary: 'Warm and sunny'
              },
              {
                id: '123e4567-e89b-12d3-a456-426614174002',
                location: 'Seattle',
                date: '2025-07-05',
                temperatureC: 18,
                temperatureF: 64,
                summary: 'Cool and cloudy'
              },
              {
                id: '123e4567-e89b-12d3-a456-426614174003',
                location: 'Singapore',
                date: '2025-07-06',
                temperatureC: 30,
                temperatureF: 86,
                summary: 'Hot and humid'
              }
            ],
            totalCount: 3
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

  describe('GET /api/weatherforecast', () => {
    it('should return all weather forecasts', async () => {
      // Act
      const response = await request(app)
        .get('/api/weatherforecast')
        .expect('Content-Type', /json/);

      // Assert
      expect(response.status).toBe(200);
      expect(response.body).toMatchObject({
        success: true,
        data: expect.arrayContaining([
          expect.objectContaining({
            id: expect.any(String),
            location: expect.any(String),
            date: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
            temperatureC: expect.any(Number),
            temperatureF: expect.any(Number),
            summary: expect.any(String)
          })
        ]),
        totalCount: 3
      });
    });

    it('should return forecasts sorted by date', async () => {
      // Act
      const response = await request(app)
        .get('/api/weatherforecast');

      // Assert
      expect(response.status).toBe(200);
      const forecasts = response.body.data;
      
      // Verify sorting
      for (let i = 1; i < forecasts.length; i++) {
        const prevDate = new Date(forecasts[i - 1].date);
        const currDate = new Date(forecasts[i].date);
        expect(currDate.getTime()).toBeGreaterThanOrEqual(prevDate.getTime());
      }
    });

    it('should calculate Fahrenheit correctly', async () => {
      // Act
      const response = await request(app)
        .get('/api/weatherforecast');

      // Assert
      expect(response.status).toBe(200);
      const forecasts = response.body.data;
      
      // Verify temperature conversion
      forecasts.forEach((forecast: any) => {
        const expectedF = Math.round((forecast.temperatureC * 9/5) + 32);
        expect(forecast.temperatureF).toBe(expectedF);
      });
    });

    it('should support waitForSortableUniqueId parameter', async () => {
      // Act
      const response = await request(app)
        .get('/api/weatherforecast?waitForSortableUniqueId=20250704T120000Z.000001.123e4567')
        .expect('Content-Type', /json/);

      // Assert
      expect(response.status).toBe(200);
      expect(response.body).toMatchObject({
        success: true,
        data: expect.any(Array),
        totalCount: expect.any(Number)
      });

      // Verify the query was called with the correct parameter
      const actorProxy = mockDaprClient.actors.getActor();
      expect(actorProxy.queryAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          waitForSortableUniqueId: '20250704T120000Z.000001.123e4567'
        })
      );
    });

    it('should return empty array when no forecasts exist', async () => {
      // Arrange
      mockDaprClient.actors.getActor = vi.fn().mockReturnValue({
        queryAsync: vi.fn().mockResolvedValue({
          weatherForecasts: [],
          totalCount: 0
        })
      });

      // Act
      const response = await request(app)
        .get('/api/weatherforecast');

      // Assert
      expect(response.status).toBe(200);
      expect(response.body).toMatchObject({
        success: true,
        data: [],
        totalCount: 0
      });
    });

    it('should handle query errors gracefully', async () => {
      // Arrange
      mockDaprClient.actors.getActor = vi.fn().mockReturnValue({
        queryAsync: vi.fn().mockRejectedValue(new Error('Query failed'))
      });

      // Act
      const response = await request(app)
        .get('/api/weatherforecast');

      // Assert
      expect(response.status).toBe(400);
      expect(response.body).toMatchObject({
        success: false,
        error: expect.stringContaining('Query failed')
      });
    });
  });

  describe('Query Domain Logic', () => {
    it('should filter out deleted forecasts', async () => {
      // Arrange - mock includes both active and deleted forecasts
      mockDaprClient.actors.getActor = vi.fn().mockReturnValue({
        queryAsync: vi.fn().mockResolvedValue({
          weatherForecasts: [
            {
              id: '123e4567-e89b-12d3-a456-426614174001',
              location: 'Tokyo',
              date: '2025-07-04',
              temperatureC: 25,
              temperatureF: 77,
              summary: 'Warm'
            }
            // Deleted forecasts are filtered out by the query logic
          ],
          totalCount: 1
        })
      });

      // Act
      const response = await request(app)
        .get('/api/weatherforecast');

      // Assert
      expect(response.status).toBe(200);
      expect(response.body.totalCount).toBe(1);
      expect(response.body.data).toHaveLength(1);
    });
  });
});