import type { Request, Response, NextFunction } from 'express';

export function traceMiddleware(req: Request, res: Response, next: NextFunction): void {
  // Extract trace context from incoming request
  const traceparent = req.headers['traceparent'] as string;
  
  if (traceparent) {
    // Echo back the trace context in the response
    res.setHeader('traceparent', traceparent);
  }
  
  next();
}