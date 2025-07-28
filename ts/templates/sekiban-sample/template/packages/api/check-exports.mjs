// Check what's actually exported from @sekiban/core
import * as sekibanCore from '@sekiban/core';

console.log('=== @sekiban/core exports ===');
console.log('Available exports:', Object.keys(sekibanCore).sort().join(', '));

// Check if defineProjector exists
console.log('\ndefineProjector:', 'defineProjector' in sekibanCore ? '✓ Available' : '✗ Not found');

// Try to import defineProjector directly
try {
  const { defineProjector } = await import('@sekiban/core');
  console.log('Direct import of defineProjector: ✓ Success');
} catch (error) {
  console.log('Direct import of defineProjector: ✗ Failed');
  console.log('Error:', error.message);
}