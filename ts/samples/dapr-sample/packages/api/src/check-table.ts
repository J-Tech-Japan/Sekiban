import { Pool } from 'pg';

async function checkTable() {
  const pool = new Pool({
    host: 'localhost',
    port: 5432,
    database: 'sekiban_events',
    user: 'sekiban',
    password: 'sekiban_password'
  });

  try {
    // Check if table exists
    const tableCheck = await pool.query(`
      SELECT EXISTS (
        SELECT FROM information_schema.tables 
        WHERE table_schema = 'public' 
        AND table_name = 'events'
      );
    `);
    console.log('Table exists:', tableCheck.rows[0].exists);

    if (tableCheck.rows[0].exists) {
      // Get column information
      const columns = await pool.query(`
        SELECT column_name, data_type, is_nullable, column_default
        FROM information_schema.columns
        WHERE table_name = 'events'
        ORDER BY ordinal_position;
      `);
      
      console.log('\nColumns in events table:');
      columns.rows.forEach(col => {
        console.log(`  ${col.column_name}: ${col.data_type} ${col.is_nullable === 'NO' ? 'NOT NULL' : 'NULL'} ${col.column_default ? `DEFAULT ${col.column_default}` : ''}`);
      });
    }

  } catch (error) {
    console.error('Error:', error);
  } finally {
    await pool.end();
  }
}

checkTable().catch(console.error);