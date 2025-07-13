import { PostgresEventStore } from '@sekiban/postgres';
import pg from 'pg';

const { Pool } = pg;

async function initializeDatabase() {
  console.log('Initializing Sekiban PostgreSQL database...');
  
  // Create a connection pool
  const pool = new Pool({
    host: 'localhost',
    port: 5432,
    database: 'sekiban_events',
    user: 'sekiban',
    password: 'sekiban_password'
  });

  try {
    // Test connection
    await pool.query('SELECT NOW()');
    console.log('Connected to PostgreSQL successfully');

    // Initialize the event store (creates tables and indexes)
    const eventStore = new PostgresEventStore(pool);
    const result = await eventStore.initialize();
    
    if (result.isOk()) {
      console.log('Database schema initialized successfully!');
      console.log('Created tables:');
      console.log('- events (with indexes)');
      console.log('- Additional tables will be created as needed');
    } else {
      console.error('Failed to initialize database:', result.error);
      process.exit(1);
    }

  } catch (error) {
    console.error('Error connecting to database:', error);
    process.exit(1);
  } finally {
    await pool.end();
  }
}

// Run the initialization
initializeDatabase().then(() => {
  console.log('Database initialization complete!');
  process.exit(0);
}).catch((error) => {
  console.error('Unexpected error:', error);
  process.exit(1);
});