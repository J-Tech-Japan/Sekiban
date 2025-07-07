import { Pool } from 'pg';

async function checkTriggers() {
  const pool = new Pool({
    host: 'localhost',
    port: 5432,
    database: 'sekiban_events',
    user: 'sekiban',
    password: 'sekiban_password'
  });

  try {
    // Check for triggers
    const triggers = await pool.query(`
      SELECT 
        trigger_name,
        event_manipulation,
        event_object_table,
        action_statement
      FROM information_schema.triggers
      WHERE event_object_table = 'events';
    `);
    
    console.log('Triggers on events table:', triggers.rows);
    
    // Check for rules
    const rules = await pool.query(`
      SELECT 
        rulename,
        definition
      FROM pg_rules
      WHERE tablename = 'events';
    `);
    
    console.log('Rules on events table:', rules.rows);
    
    // Check for constraints
    const constraints = await pool.query(`
      SELECT 
        constraint_name,
        constraint_type
      FROM information_schema.table_constraints
      WHERE table_name = 'events';
    `);
    
    console.log('Constraints on events table:', constraints.rows);

  } catch (error) {
    console.error('Error:', error);
  } finally {
    await pool.end();
  }
}

checkTriggers().catch(console.error);