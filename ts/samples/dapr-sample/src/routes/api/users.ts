import { Router } from 'express';
import type { Request, Response } from 'express';
import type { SimpleSekibanExecutor } from '../../infrastructure/simple-multi-payload-executor.js';
import { 
  CreateUserCommand, 
  ConfirmUserCommand, 
  UpdateUserNameCommand,
  UserProjector
} from '@sekiban/dapr-sample-domain';

export function createUserApiRoutes(executor: SimpleSekibanExecutor): Router {
  const router = Router();
  const userProjector = new UserProjector();
  
  // POST /api/users/create - Create a new user (follows C# template pattern)
  router.post('/create', async (req: Request, res: Response) => {
    try {
      const { name, email } = req.body;
      
      // Validate and create command
      const commandResult = CreateUserCommand.create({ name, email });
      if (commandResult.isErr()) {
        return res.status(400).json({
          success: false,
          error: commandResult.error.message,
          details: commandResult.error.details
        });
      }
      
      const command = commandResult.value;
      
      // Execute command
      const result = await executor.executeCommand(command);
      if (result.isErr()) {
        const error = result.error;
        if (error.message.includes('already exists')) {
          return res.status(409).json({
            success: false,
            error: 'User with this email already exists'
          });
        }
        return res.status(500).json({
          success: false,
          error: 'Internal server error',
          details: error.message
        });
      }
      
      const commandResult_1 = result.value;
      
      return res.status(201).json({
        success: true,
        id: commandResult_1.aggregateId,
        lastSortableUniqueId: commandResult_1.lastSortableUniqueId
      });
      
    } catch (error) {
      console.error('Error creating user:', error);
      return res.status(500).json({
        success: false,
        error: 'Internal server error'
      });
    }
  });
  
  // POST /api/users/confirm - Confirm a user (state transition)
  router.post('/confirm', async (req: Request, res: Response) => {
    try {
      const { userId } = req.body;
      
      // Validate and create command
      const commandResult = ConfirmUserCommand.create({ userId });
      if (commandResult.isErr()) {
        return res.status(400).json({
          success: false,
          error: commandResult.error.message,
          details: commandResult.error.details
        });
      }
      
      const command = commandResult.value;
      
      // Execute command
      const result = await executor.executeCommand(command);
      if (result.isErr()) {
        const error = result.error;
        if (error.message.includes('not found') || error.message.includes('does not exist')) {
          return res.status(404).json({
            success: false,
            error: 'User not found'
          });
        }
        if (error.message.includes('UnconfirmedUser')) {
          return res.status(400).json({
            success: false,
            error: error.message
          });
        }
        return res.status(500).json({
          success: false,
          error: 'Internal server error',
          details: error.message
        });
      }
      
      const commandResult_1 = result.value;
      
      return res.status(200).json({
        success: true,
        lastSortableUniqueId: commandResult_1.lastSortableUniqueId
      });
      
    } catch (error) {
      console.error('Error confirming user:', error);
      return res.status(500).json({
        success: false,
        error: 'Internal server error'
      });
    }
  });
  
  // POST /api/users/update-name - Update user name
  router.post('/update-name', async (req: Request, res: Response) => {
    try {
      const { userId, newName } = req.body;
      
      // Validate and create command
      const commandResult = UpdateUserNameCommand.create({ userId, newName });
      if (commandResult.isErr()) {
        return res.status(400).json({
          success: false,
          error: commandResult.error.message,
          details: commandResult.error.details
        });
      }
      
      const command = commandResult.value;
      
      // Execute command
      const result = await executor.executeCommand(command);
      if (result.isErr()) {
        const error = result.error;
        if (error.message.includes('not found') || error.message.includes('does not exist')) {
          return res.status(404).json({
            success: false,
            error: 'User not found'
          });
        }
        return res.status(500).json({
          success: false,
          error: 'Internal server error',
          details: error.message
        });
      }
      
      const commandResult_1 = result.value;
      
      return res.status(200).json({
        success: true,
        lastSortableUniqueId: commandResult_1.lastSortableUniqueId
      });
      
    } catch (error) {
      console.error('Error updating user name:', error);
      return res.status(500).json({
        success: false,
        error: 'Internal server error'
      });
    }
  });
  
  // GET /api/users/:id - Get user by ID (query endpoint)
  router.get('/:id', async (req: Request, res: Response) => {
    try {
      const userId = req.params.id;
      const waitForSortableUniqueId = req.query.waitForSortableUniqueId as string;
      
      if (!userId) {
        return res.status(400).json({
          success: false,
          error: 'User ID is required'
        });
      }
      
      // TODO: Handle waitForSortableUniqueId for eventual consistency
      
      // Get aggregate
      const result = await executor.getAggregate(userProjector, userId);
      if (result.isErr()) {
        return res.status(500).json({
          success: false,
          error: 'Internal server error',
          details: result.error.message
        });
      }
      
      const aggregate = result.value;
      
      // Check if user exists (not empty)
      if (!aggregate.payload || aggregate.payload.aggregateType === 'Empty') {
        return res.status(404).json({
          success: false,
          error: 'User not found'
        });
      }
      
      return res.status(200).json(aggregate.payload);
      
    } catch (error) {
      console.error('Error getting user:', error);
      return res.status(500).json({
        success: false,
        error: 'Internal server error'
      });
    }
  });
  
  return router;
}