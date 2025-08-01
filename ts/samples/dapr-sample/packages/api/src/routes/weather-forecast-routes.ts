import { Router, Request, Response, NextFunction } from 'express';
import type { Router as ExpressRouter } from 'express';
import { z } from 'zod';
import { 
  InputWeatherForecast,
  UpdateWeatherForecastLocation,
  DeleteWeatherForecast,
  WeatherForecastQuery,
  createTemperatureCelsius
} from '@dapr-sample/domain';
import { CommandValidationError, AggregateNotFoundError } from '@sekiban/core';
import { getExecutor } from '../setup/executor.js';

const router: ExpressRouter = Router();

// Helper to create HTTP errors
class HttpError extends Error {
  constructor(message: string, public statusCode: number, public code: string) {
    super(message);
    this.name = 'HttpError';
  }
}

// Input weather forecast
router.post(
  '/weatherforecast/input',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      // Validate request body
      const schema = z.object({
        location: z.string(),
        date: z.string(),
        temperatureC: z.number(),
        summary: z.string().optional()
      });
      
      const parseResult = schema.safeParse(req.body);
      if (!parseResult.success) {
        const validationErrors = parseResult.error.issues.map(issue => 
          `${issue.path.join('.')}: ${issue.message}`
        );
        const error = new CommandValidationError('InputWeatherForecast', validationErrors);
        return next(error);
      }

      const executor = await getExecutor();
      const command = InputWeatherForecast.create(parseResult.data);
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        return next(result.error);
      }

      res.json({
        success: true,
        aggregateId: result.value.aggregateId,
        version: result.value.version
      });
    } catch (error) {
      next(error);
    }
  }
);

// Update weather forecast location
router.post(
  '/weatherforecast/:weatherForecastId/update-location',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { weatherForecastId } = req.params;
      
      // Validate UUID
      const uuidResult = z.string().uuid().safeParse(weatherForecastId);
      if (!uuidResult.success) {
        const error = new CommandValidationError('WeatherForecastId', ['Invalid weather forecast ID format']);
        return next(error);
      }

      // Validate request body
      const schema = z.object({
        location: z.string()
      });
      
      const parseResult = schema.safeParse(req.body);
      if (!parseResult.success) {
        const validationErrors = parseResult.error.issues.map(issue => 
          `${issue.path.join('.')}: ${issue.message}`
        );
        const error = new CommandValidationError('UpdateLocation', validationErrors);
        return next(error);
      }

      const executor = await getExecutor();
      const command = UpdateWeatherForecastLocation.create({
        weatherForecastId,
        location: parseResult.data.location
      });
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        return next(result.error);
      }

      res.json({
        success: true,
        aggregateId: weatherForecastId,
        version: result.value.version
      });
    } catch (error) {
      next(error);
    }
  }
);

// Delete weather forecast
router.post(
  '/weatherforecast/:weatherForecastId/delete',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { weatherForecastId } = req.params;
      
      // Validate UUID
      const uuidResult = z.string().uuid().safeParse(weatherForecastId);
      if (!uuidResult.success) {
        const error = new CommandValidationError('WeatherForecastId', ['Invalid weather forecast ID format']);
        return next(error);
      }

      const executor = await getExecutor();
      const command = DeleteWeatherForecast.create({ weatherForecastId });
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        return next(result.error);
      }

      res.json({
        success: true,
        aggregateId: weatherForecastId,
        version: result.value.version
      });
    } catch (error) {
      next(error);
    }
  }
);

// Remove weather forecast (alias for delete)
router.post(
  '/weatherforecast/:weatherForecastId/remove',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { weatherForecastId } = req.params;
      
      // Validate UUID
      const uuidResult = z.string().uuid().safeParse(weatherForecastId);
      if (!uuidResult.success) {
        const error = new CommandValidationError('WeatherForecastId', ['Invalid weather forecast ID format']);
        return next(error);
      }

      const executor = await getExecutor();
      const command = DeleteWeatherForecast.create({ weatherForecastId });
      const result = await executor.executeCommandAsync(command);

      if (result.isErr()) {
        return next(result.error);
      }

      res.json({
        success: true,
        aggregateId: weatherForecastId,
        version: result.value.version
      });
    } catch (error) {
      next(error);
    }
  }
);

// Get all weather forecasts
router.get(
  '/weatherforecast',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { waitForSortableUniqueId } = req.query;
      const executor = await getExecutor();
      
      const query = new WeatherForecastQuery();
      
      // Execute multi-projection query
      let result;
      try {
        result = await executor.queryAsync(query);
      } catch (error) {
        return res.status(500).json({ 
          error: 'Failed to execute query',
          details: error instanceof Error ? error.message : String(error)
        });
      }
      
      if (result.isErr()) {
        return res.status(500).json({ 
          error: 'Failed to fetch weather forecasts',
          details: result.error.message
        });
      }
      
      const queryResult = result.value || [];
      res.json(queryResult);
    } catch (error) {
      next(error);
    }
  }
);

// Generate sample weather data
router.post(
  '/weatherforecast/generate',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];
      const random = () => Math.random();
      const commands: any[] = [];
      const executor = await getExecutor();

      for (const city of ["Seattle", "Tokyo", "Singapore", "Sydney", "London"]) {
        for (let i = 0; i < 3; i++) {
          const date = new Date();
          date.setDate(date.getDate() + i);
          const temperatureC = Math.floor(random() * 75) - 20; // -20 to 55
          const command = InputWeatherForecast.create({
            location: city,
            date: date.toISOString().split('T')[0],
            temperatureC,
            summary: summaries[Math.floor(random() * summaries.length)]
          });
          commands.push(command);
        }
      }

      const results: any[] = [];
      for (const command of commands) {
        const result = await executor.executeCommandAsync(command);
        if (result.isOk()) {
          results.push({
            success: true,
            aggregateId: result.value.aggregateId,
            version: result.value.version
          });
        } else {
          results.push({
            success: false,
            error: result.error.message
          });
        }
      }

      res.json({ 
        message: "Sample weather data generated", 
        count: results.length,
        results 
      });
    } catch (error) {
      next(error);
    }
  }
);

// Get individual weather forecast aggregate state (for debugging)
router.get(
  '/weatherforecast/:weatherForecastId/aggregate-state',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { weatherForecastId } = req.params;
      
      // Validate UUID
      const uuidResult = z.string().uuid().safeParse(weatherForecastId);
      if (!uuidResult.success) {
        const error = new CommandValidationError('WeatherForecastId', ['Invalid weather forecast ID format']);
        return next(error);
      }

      const { DaprClient, ActorProxyBuilder, ActorId } = await import('@dapr/dapr');
      const { AggregateActorFactory } = await import('@sekiban/dapr');
      
      const daprClient = new DaprClient({
        daprHost: "127.0.0.1",
        daprPort: String(process.env.DAPR_HTTP_PORT || "3500")
      });
      
      const AggregateActorClass = AggregateActorFactory.createActorClass();
      const builder = new ActorProxyBuilder(AggregateActorClass, daprClient);
      
      // Use the same actor ID pattern as in command execution
      const actorId = `default@WeatherForecast@${weatherForecastId}=WeatherForecastProjector`;
      const actor = builder.build(new ActorId(actorId)) as any;
      
      console.log(`[GET] Getting aggregate state for actor: ${actorId}`);
      
      try {
        const aggregateState = await actor.getAggregateStateAsync();
        console.log('[GET] Aggregate state loaded:', JSON.stringify(aggregateState, null, 2));
        
        if (!aggregateState) {
          const error = new AggregateNotFoundError(weatherForecastId, 'WeatherForecast');
          return next(error);
        }
        
        res.json({ 
          success: true,
          aggregateId: weatherForecastId,
          actorId,
          aggregateState
        });
      } catch (actorError) {
        console.error('[GET] Error calling getAggregateStateAsync:', actorError);
        const error = new AggregateNotFoundError(weatherForecastId, 'WeatherForecast');
        return next(error);
      }
    } catch (error) {
      next(error);
    }
  }
);

// Check weather forecast version (simplified version check)
router.get(
  '/weatherforecast/:weatherForecastId/version',
  async (req: Request, res: Response, next: NextFunction) => {
    try {
      const { weatherForecastId } = req.params;
      
      // Validate UUID
      const uuidResult = z.string().uuid().safeParse(weatherForecastId);
      if (!uuidResult.success) {
        const error = new CommandValidationError('WeatherForecastId', ['Invalid weather forecast ID format']);
        return next(error);
      }

      const { DaprClient, ActorProxyBuilder, ActorId } = await import('@dapr/dapr');
      const { AggregateActorFactory } = await import('@sekiban/dapr');
      
      const daprClient = new DaprClient({
        daprHost: "127.0.0.1",
        daprPort: String(process.env.DAPR_HTTP_PORT || "3500")
      });
      
      const AggregateActorClass = AggregateActorFactory.createActorClass();
      const builder = new ActorProxyBuilder(AggregateActorClass, daprClient);
      
      const actorId = `default@WeatherForecast@${weatherForecastId}=WeatherForecastProjector`;
      const actor = builder.build(new ActorId(actorId)) as any;
      
      try {
        const aggregateState = await actor.getAggregateStateAsync();
        
        if (!aggregateState) {
          res.json({ 
            success: true,
            aggregateId: weatherForecastId,
            version: 0,
            exists: false
          });
        } else {
          res.json({ 
            success: true,
            aggregateId: weatherForecastId,
            version: aggregateState.version || 0,
            exists: true
          });
        }
      } catch (actorError) {
        res.json({ 
          success: true,
          aggregateId: weatherForecastId,
          version: 0,
          exists: false,
          error: actorError instanceof Error ? actorError.message : String(actorError)
        });
      }
    } catch (error) {
      next(error);
    }
  }
);

export { router as weatherForecastRoutes };