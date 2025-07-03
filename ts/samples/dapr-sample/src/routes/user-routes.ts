import { Router } from 'express';
import type { Request, Response } from 'express';
import type { SekibanExecutor } from '../infrastructure/simple-sekiban-executor';
import { CreateUserCommand } from '../domain/user/commands/create-user-command';
import { GetUserQuery } from '../domain/user/queries/get-user-query';
import { validateCreateUserRequest } from '../validators/user-validators';

export function createUserRoutes(executor: SekibanExecutor): Router {
  const router = Router();
  
  // POST /users - Create a new user
  router.post('/', async (req: Request, res: Response) => {
    try {
      // Validate request
      const validationResult = validateCreateUserRequest(req.body);
      if (!validationResult.success) {
        return res.status(400).json({
          error: 'Validation failed',
          details: validationResult.errors
        });
      }
      
      const { name, email } = validationResult.data;
      
      // Execute command
      const command = new CreateUserCommand(name, email);
      const result = await executor.executeCommand(command);
      
      if (result.isErr()) {
        const error = result.error;
        if (error.message.includes('duplicate') || error.message.includes('already exists')) {
          return res.status(409).json({
            error: 'User with this email already exists'
          });
        }
        return res.status(500).json({
          error: 'Internal server error',
          details: error.message
        });
      }
      
      const commandResult = result.value;
      
      return res.status(201).json({
        success: true,
        id: commandResult.aggregateId
      });
      
    } catch (error) {
      console.error('Error creating user:', error);
      return res.status(500).json({
        error: 'Internal server error'
      });
    }
  });
  
  // GET /users/:id - Get user by ID (with optional time-travel support)
  router.get('/:id', async (req: Request, res: Response) => {
    try {
      const userId = req.params.id;
      const asOf = req.query.asOf as string;
      
      if (!userId) {
        return res.status(400).json({
          error: 'User ID is required'
        });
      }
      
      // Execute query (with optional time-travel parameter)
      const query = new GetUserQuery(userId, asOf);
      const result = await executor.executeQuery(query);
      
      if (result.isErr()) {
        const error = result.error;
        if (error.message.includes('not found')) {
          return res.status(404).json({
            error: 'User not found'
          });
        }
        return res.status(500).json({
          error: 'Internal server error',
          details: error.message
        });
      }
      
      const user = result.value;
      
      return res.status(200).json(user);
      
    } catch (error) {
      console.error('Error getting user:', error);
      return res.status(500).json({
        error: 'Internal server error'
      });
    }
  });
  
  return router;
}