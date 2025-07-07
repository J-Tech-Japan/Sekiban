import { Request, Response, NextFunction } from 'express';
import { config } from '../config/index.js';

export interface ApiError extends Error {
  statusCode?: number;
  code?: string;
  details?: any;
}

export function errorHandler(
  err: ApiError,
  req: Request,
  res: Response,
  next: NextFunction
): void {
  const statusCode = err.statusCode || 500;
  const message = err.message || 'Internal Server Error';
  const code = err.code || 'INTERNAL_ERROR';

  // Log error
  if (statusCode >= 500) {
    console.error('Server error:', {
      error: err,
      request: {
        method: req.method,
        url: req.url,
        headers: req.headers,
        body: req.body
      }
    });
  }

  // Send error response
  res.status(statusCode).json({
    error: {
      code,
      message,
      ...(config.NODE_ENV === 'development' && {
        stack: err.stack,
        details: err.details
      })
    }
  });
}