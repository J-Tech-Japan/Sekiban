import { Pool } from 'pg';

async function createTable() {
  const pool = new Pool({
    host: 'localhost',
    port: 5432,
    database: 'sekiban_events',
    user: 'sekiban',
    password: 'sekiban_password'
  });

  try {
    console.log('Creating events table with correct structure...');
    
    // Create events table matching C# DbEvent structure
    await pool.query(`
      CREATE TABLE IF NOT EXISTS events (
        id UUID PRIMARY KEY,
        payload JSON NOT NULL,
        sortable_unique_id VARCHAR(255) NOT NULL,
        version INTEGER NOT NULL,
        aggregate_id UUID NOT NULL,
        root_partition_key VARCHAR(255) NOT NULL,
        "timestamp" TIMESTAMP NOT NULL,
        partition_key VARCHAR(255) NOT NULL,
        aggregate_group VARCHAR(255) NOT NULL,
        payload_type_name VARCHAR(255) NOT NULL,
        causation_id VARCHAR(255) NOT NULL DEFAULT '',
        correlation_id VARCHAR(255) NOT NULL DEFAULT '',
        executed_user VARCHAR(255) NOT NULL DEFAULT ''
      )
    `);
    
    console.log('Table created successfully');
    
    // Create indexes
    await pool.query(`CREATE INDEX IF NOT EXISTS idx_events_partition_key ON events(partition_key)`);
    await pool.query(`CREATE INDEX IF NOT EXISTS idx_events_root_partition ON events(root_partition_key)`);
    await pool.query(`CREATE INDEX IF NOT EXISTS idx_events_aggregate ON events(aggregate_group, aggregate_id)`);
    await pool.query(`CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events("timestamp")`);
    await pool.query(`CREATE INDEX IF NOT EXISTS idx_events_sortable_unique_id ON events(sortable_unique_id)`);
    
    console.log('Indexes created successfully');

  } catch (error) {
    console.error('Error:', error);
  } finally {
    await pool.end();
  }
}

createTable().catch(console.error);