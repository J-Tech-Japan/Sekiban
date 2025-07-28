import { Request, Response, NextFunction } from 'express';
import { SekibanError } from '@sekiban/core';
import { config } from '../config/index.js';

export function errorHandler(
  err: Error | SekibanError,
  req: Request,
  res: Response,
  next: NextFunction
): void {
  // Determine status code based on error type
  let statusCode = 500;
  let code = 'INTERNAL_ERROR';
  let details: any = undefined;

  if (err instanceof SekibanError) {
    // Map Sekiban error codes to HTTP status codes
    switch (err.code) {
      case 'AGGREGATE_NOT_FOUND':
        statusCode = 404;
        break;
      case 'COMMAND_VALIDATION_ERROR':
        statusCode = 400;
        break;
      case 'UNAUTHORIZED':
        statusCode = 401;
        break;
      case 'FORBIDDEN':
        statusCode = 403;
        break;
      case 'CONFLICT':
        statusCode = 409;
        break;
      default:
        statusCode = 500;
    }
    code = err.code;
    
    // Extract validation errors if available
    if ('validationErrors' in err) {
      details = { validationErrors: (err as any).validationErrors };
    }
  } else if ('statusCode' in err && typeof (err as any).statusCode === 'number') {
    // Handle custom errors with statusCode property
    statusCode = (err as any).statusCode;
    code = (err as any).code || 'UNKNOWN_ERROR';
    details = (err as any).details;
  }

  const message = err.message || 'Internal Server Error';

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
        details
      })
    }
  });
}