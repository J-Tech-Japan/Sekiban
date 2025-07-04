/**
 * Weather Forecast Dapr Sample Application
 * 
 * Demonstrates Sekiban TypeScript integration with Dapr using:
 * - Multi-payload aggregate projectors (WeatherForecast <-> DeletedWeatherForecast)
 * - Event sourcing with CQRS patterns
 * - Dapr actors for distributed aggregate management
 * - State machine patterns for domain modeling
 */

import { createApp } from './app.js';

async function startServer(): Promise<void> {
  try {
    const port = process.env.PORT || 5000;
    
    console.log('🌤️  Starting Weather Forecast Dapr Sample...');
    console.log(`📍 Environment: ${process.env.NODE_ENV || 'development'}`);
    console.log(`🔗 Dapr Host: ${process.env.DAPR_HOST || 'localhost'}`);
    console.log(`🚪 Dapr HTTP Port: ${process.env.DAPR_HTTP_PORT || '3500'}`);
    console.log(`🏪 State Store: ${process.env.DAPR_STATE_STORE || 'sekiban-eventstore'}`);
    console.log(`📨 PubSub: ${process.env.DAPR_PUBSUB || 'sekiban-pubsub'}`);
    
    // Create Express application with Sekiban Dapr integration
    const app = await createApp();
    
    // Start the server
    const server = app.listen(port, () => {
      console.log(`🚀 Server running on port ${port}`);
      console.log(`📖 Health check: http://localhost:${port}/healthz`);
      console.log(`📊 Metrics: http://localhost:${port}/metrics`);
      console.log(`🌤️  Weather API: http://localhost:${port}/api/weatherforecast`);
      console.log(`🔍 Debug info: http://localhost:${port}/debug/env`);
      console.log('');
      console.log('Sample API calls:');
      console.log(`  POST http://localhost:${port}/api/weatherforecast/input`);
      console.log(`  GET  http://localhost:${port}/api/weatherforecast`);
      console.log(`  POST http://localhost:${port}/api/weatherforecast/generate`);
      console.log('');
      console.log('✅ Application ready!');
    });

    // Graceful shutdown
    process.on('SIGTERM', () => {
      console.log('🛑 SIGTERM received, shutting down gracefully...');
      server.close(() => {
        console.log('👋 Server closed');
        process.exit(0);
      });
    });

    process.on('SIGINT', () => {
      console.log('🛑 SIGINT received, shutting down gracefully...');
      server.close(() => {
        console.log('👋 Server closed');
        process.exit(0);
      });
    });

  } catch (error) {
    console.error('❌ Failed to start server:', error);
    process.exit(1);
  }
}

// Start the application
startServer().catch((error) => {
  console.error('💥 Unhandled error during startup:', error);
  process.exit(1);
});