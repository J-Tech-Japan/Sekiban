import type { Request, Response, NextFunction } from 'express';
import type { MetricsStore } from '../observability/metrics-store';

export function createMetricsMiddleware(metricsStore: MetricsStore) {
  return (req: Request, res: Response, next: NextFunction): void => {
    const startTime = Date.now();

    res.on('finish', () => {
      const duration = (Date.now() - startTime) / 1000; // Convert to seconds
      
      // Record HTTP request duration
      metricsStore.recordHistogram('http_request_duration_seconds', duration, {
        method: req.method,
        route: req.route?.path || req.path,
        status_code: res.statusCode.toString()
      });
    });

    next();
  };
}