import { Router } from 'express';
import type { Request, Response } from 'express';
import type { ISekibanDaprExecutor } from '@sekiban/dapr';
import { 
  InputWeatherForecastCommand,
  UpdateWeatherForecastLocationCommand,
  DeleteWeatherForecastCommand,
  RemoveWeatherForecastCommand,
  WeatherForecastQuery,
  TemperatureCelsius
} from '@sekiban/dapr-sample-domain';

interface InputWeatherForecastRequest {
  location: string;
  date: string;
  temperatureC: number;
  summary?: string;
}

interface UpdateLocationRequest {
  location: string;
}

export function createWeatherForecastRoutes(executor: ISekibanDaprExecutor): Router {
  const router = Router();

  /**
   * POST /input - Create a new weather forecast
   * Matches C# InputWeatherForecast endpoint
   */
  router.post('/input', async (req: Request<{}, {}, InputWeatherForecastRequest>, res: Response) => {
    try {
      const { location, date, temperatureC, summary } = req.body;

      // Validate required fields
      if (!location || !date || temperatureC === undefined) {
        return res.status(400).json({
          success: false,
          error: 'Missing required fields: location, date, temperatureC'
        });
      }

      // Create command using static factory
      const commandResult = InputWeatherForecastCommand.create({
        location,
        date,
        temperatureC,
        summary
      });

      if (commandResult.isErr()) {
        return res.status(400).json({
          success: false,
          error: 'Invalid command data',
          details: commandResult.error.details
        });
      }

      const command = commandResult.value;
      
      // Execute command through Dapr executor
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        return res.status(400).json({
          success: false,
          error: result.error.message
        });
      }

      const response = result.value;
      
      // Check if command execution failed
      if (!response.success) {
        return res.status(400).json({
          success: false,
          error: response.errorMessage || 'Command execution failed'
        });
      }
      
      return res.status(200).json({
        success: response.success,
        aggregateId: response.aggregateId,
        lastSortableUniqueId: response.lastSortableUniqueId
      });

    } catch (error) {
      console.error('Error in InputWeatherForecast:', error);
      return res.status(500).json({
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      });
    }
  });

  /**
   * POST /:weatherForecastId/update-location - Update weather forecast location
   * Matches C# UpdateWeatherForecastLocation endpoint
   */
  router.post('/:weatherForecastId/update-location', async (
    req: Request<{ weatherForecastId: string }, {}, UpdateLocationRequest>, 
    res: Response
  ) => {
    try {
      const { weatherForecastId } = req.params;
      const { location } = req.body;

      if (!location) {
        return res.status(400).json({
          success: false,
          error: 'Missing required field: location'
        });
      }

      // Create command using static factory
      const commandResult = UpdateWeatherForecastLocationCommand.create({
        weatherForecastId,
        location
      });

      if (commandResult.isErr()) {
        return res.status(400).json({
          success: false,
          error: 'Invalid command data',
          details: commandResult.error.details
        });
      }

      const command = commandResult.value;
      
      // Execute command through Dapr executor
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        return res.status(400).json({
          success: false,
          error: result.error.message
        });
      }

      const response = result.value;
      
      // Check if command execution failed
      if (!response.success) {
        return res.status(400).json({
          success: false,
          error: response.errorMessage || 'Command execution failed'
        });
      }
      
      return res.status(200).json({
        success: response.success,
        aggregateId: response.aggregateId,
        lastSortableUniqueId: response.lastSortableUniqueId
      });

    } catch (error) {
      console.error('Error in UpdateWeatherForecastLocation:', error);
      return res.status(500).json({
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      });
    }
  });

  /**
   * POST /:weatherForecastId/delete - Delete weather forecast
   * Matches C# DeleteWeatherForecast endpoint
   */
  router.post('/:weatherForecastId/delete', async (
    req: Request<{ weatherForecastId: string }>, 
    res: Response
  ) => {
    try {
      const { weatherForecastId } = req.params;

      // Create command using static factory
      const commandResult = DeleteWeatherForecastCommand.create({
        weatherForecastId
      });

      if (commandResult.isErr()) {
        return res.status(400).json({
          success: false,
          error: 'Invalid command data',
          details: commandResult.error.details
        });
      }

      const command = commandResult.value;
      
      // Execute command through Dapr executor
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        return res.status(400).json({
          success: false,
          error: result.error.message
        });
      }

      const response = result.value;
      
      // Check if command execution failed
      if (!response.success) {
        return res.status(400).json({
          success: false,
          error: response.errorMessage || 'Command execution failed'
        });
      }
      
      return res.status(200).json({
        success: response.success,
        aggregateId: response.aggregateId,
        lastSortableUniqueId: response.lastSortableUniqueId
      });

    } catch (error) {
      console.error('Error in DeleteWeatherForecast:', error);
      return res.status(500).json({
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      });
    }
  });

  /**
   * POST /:weatherForecastId/remove - Remove weather forecast (hard delete)
   * Matches C# RemoveWeatherForecast endpoint
   */
  router.post('/:weatherForecastId/remove', async (
    req: Request<{ weatherForecastId: string }>, 
    res: Response
  ) => {
    try {
      const { weatherForecastId } = req.params;

      // Create command using static factory
      const commandResult = RemoveWeatherForecastCommand.create({
        weatherForecastId
      });

      if (commandResult.isErr()) {
        return res.status(400).json({
          success: false,
          error: 'Invalid command data',
          details: commandResult.error.details
        });
      }

      const command = commandResult.value;
      
      // Execute command through Dapr executor
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        return res.status(400).json({
          success: false,
          error: result.error.message
        });
      }

      const response = result.value;
      
      // Check if command execution failed
      if (!response.success) {
        return res.status(400).json({
          success: false,
          error: response.errorMessage || 'Command execution failed'
        });
      }
      
      return res.status(200).json({
        success: response.success,
        aggregateId: response.aggregateId,
        lastSortableUniqueId: response.lastSortableUniqueId
      });

    } catch (error) {
      console.error('Error in RemoveWeatherForecast:', error);
      return res.status(500).json({
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      });
    }
  });

  /**
   * GET / - Get all weather forecasts
   * Matches C# GetWeatherForecasts endpoint
   */
  router.get('/', async (req: Request, res: Response) => {
    try {
      const { waitForSortableUniqueId } = req.query;

      // Create query
      const query = WeatherForecastQuery.create({
        waitForSortableUniqueId: waitForSortableUniqueId as string
      });
      
      // Execute query through Dapr executor
      const result = await executor.queryAsync(query);

      if (result.isErr()) {
        return res.status(400).json({
          success: false,
          error: result.error.message
        });
      }

      const queryResult = result.value;
      return res.status(200).json({
        success: true,
        data: queryResult.weatherForecasts,
        totalCount: queryResult.totalCount
      });

    } catch (error) {
      console.error('Error in GetWeatherForecasts:', error);
      return res.status(500).json({
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      });
    }
  });

  /**
   * POST /generate - Generate sample weather data
   * Matches C# GenerateWeatherData endpoint
   */
  router.post('/generate', async (req: Request, res: Response) => {
    try {
      const summaries = ['Freezing', 'Bracing', 'Chilly', 'Cool', 'Mild', 'Warm', 'Balmy', 'Hot', 'Sweltering', 'Scorching'];
      const cities = ['Seattle', 'Tokyo', 'Singapore', 'Sydney', 'London'];
      const results = [];

      for (const city of cities) {
        for (let i = 0; i < 3; i++) {
          const date = new Date();
          date.setDate(date.getDate() + i);
          const dateString = date.toISOString().split('T')[0]; // YYYY-MM-DD format

          const temperatureC = Math.floor(Math.random() * 75) - 20; // -20 to 55
          const summary = summaries[Math.floor(Math.random() * summaries.length)];

          const commandResult = InputWeatherForecastCommand.create({
            location: city,
            date: dateString,
            temperatureC,
            summary
          });

          if (commandResult.isOk()) {
            const command = commandResult.value;
            const result = await executor.executeCommandAsync(command);
            
            if (result.isOk()) {
              results.push({
                location: city,
                date: dateString,
                temperatureC,
                summary,
                aggregateId: result.value.aggregateId
              });
            }
          }
        }
      }

      return res.status(200).json({
        success: true,
        message: 'Sample weather data generated',
        count: results.length,
        forecasts: results
      });

    } catch (error) {
      console.error('Error in GenerateWeatherData:', error);
      return res.status(500).json({
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      });
    }
  });

  return router;
}