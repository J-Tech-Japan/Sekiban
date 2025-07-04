// Health checking utilities
export interface HealthCheck {
  name: string;
  check: () => Promise<{ status: 'ok' | 'error'; error?: string }>;
}

export interface HealthResult {
  status: 'ok' | 'not_ready';
  checks: Record<string, 'ok' | 'error'>;
  errors?: string[];
  timestamp: string;
}

export class HealthChecker {
  private checks: HealthCheck[] = [];

  addCheck(check: HealthCheck): void {
    this.checks.push(check);
  }

  async checkLiveness(): Promise<{ status: 'ok'; timestamp: string }> {
    // Liveness should always return ok unless the process is completely broken
    return {
      status: 'ok',
      timestamp: new Date().toISOString()
    };
  }

  async checkReadiness(): Promise<HealthResult> {
    const results: Record<string, 'ok' | 'error'> = {};
    const errors: string[] = [];

    for (const check of this.checks) {
      try {
        const result = await check.check();
        results[check.name] = result.status;
        if (result.status === 'error' && result.error) {
          errors.push(result.error);
        }
      } catch (error) {
        results[check.name] = 'error';
        errors.push(`${check.name}: ${error instanceof Error ? error.message : 'Unknown error'}`);
      }
    }

    const allOk = Object.values(results).every(status => status === 'ok');

    return {
      status: allOk ? 'ready' : 'not_ready',
      checks: results,
      errors: errors.length > 0 ? errors : undefined,
      timestamp: new Date().toISOString()
    };
  }
}