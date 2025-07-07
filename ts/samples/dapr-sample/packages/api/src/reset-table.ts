import { Pool } from 'pg';

async function resetTable() {
  const pool = new Pool({
    host: 'localhost',
    port: 5432,
    database: 'sekiban_events',
    user: 'sekiban',
    password: 'sekiban_password'
  });

  try {
    console.log('Dropping existing events table...');
    await pool.query('DROP TABLE IF EXISTS events CASCADE');
    console.log('Table dropped successfully');

  } catch (error) {
    console.error('Error:', error);
  } finally {
    await pool.end();
  }
}

resetTable().catch(console.error);