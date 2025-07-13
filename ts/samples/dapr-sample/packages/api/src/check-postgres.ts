import pg from 'pg';

const { Client } = pg;

const client = new Client({
  host: 'localhost',
  port: 5432,
  database: 'sekiban_events',
  user: 'sekiban',
  password: 'sekiban_password'
});

async function checkEvents() {
  try {
    await client.connect();
    console.log('Connected to PostgreSQL');
    
    // First check table structure
    const tableInfo = await client.query(`
      SELECT column_name, data_type 
      FROM information_schema.columns 
      WHERE table_name = 'events'
      ORDER BY ordinal_position
    `);
    console.log('\nTable structure:');
    tableInfo.rows.forEach(col => {
      console.log(`- ${col.column_name}: ${col.data_type}`);
    });
    
    const res = await client.query('SELECT * FROM events ORDER BY id DESC LIMIT 10');
    console.log('\nEvents in PostgreSQL:');
    console.log('Total row count (up to 10):', res.rowCount);
    
    // Also check total count
    const countRes = await client.query('SELECT COUNT(*) as total FROM events');
    console.log('Total events in database:', countRes.rows[0].total);
    
    if (res.rowCount === 0) {
      console.log('No events found in the database');
    } else {
      res.rows.forEach((row, i) => {
        console.log(`\n--- Event ${i + 1} ---`);
        console.log('ID:', row.id);
        console.log('Payload Type:', row.payload_type_name);
        console.log('Aggregate ID:', row.aggregate_id);
        console.log('Aggregate Group:', row.aggregate_group);
        console.log('Version:', row.version);
        console.log('Timestamp:', row.timestamp);
        console.log('Payload:', JSON.stringify(row.payload, null, 2));
      });
    }
  } catch (err: any) {
    console.error('Error:', err.message);
  } finally {
    await client.end();
  }
}

checkEvents();