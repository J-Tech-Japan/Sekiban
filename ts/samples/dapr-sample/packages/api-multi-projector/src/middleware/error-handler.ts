import { Request, Response, NextFunction } from 'express';
import logger from '../utils/logger.js';

interface ErrorWithStatus extends Error {
  status?: number;
}

export const errorHandler = (
  err: ErrorWithStatus,
  req: Request,
  res: Response,
  _next: NextFunction
) => {
  logger.error(`Error handling request ${req.method} ${req.path}:`, err.message);
  
  const status = err.status || 500;
  const message = err.message || 'Internal server error';
  
  res.status(status).json({
    error: {
      message,
      status,
      timestamp: new Date().toISOString(),
    },
  });
};