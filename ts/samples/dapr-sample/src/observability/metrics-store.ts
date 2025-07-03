// Simple in-memory metrics store for TDD
export class MetricsStore {
  private counters: Map<string, number> = new Map();
  private histograms: Map<string, { count: number; sum: number; buckets: Map<number, number> }> = new Map();

  incrementCounter(name: string, labels: Record<string, string> = {}): void {
    const key = this.getMetricKey(name, labels);
    const current = this.counters.get(key) || 0;
    this.counters.set(key, current + 1);
  }

  recordHistogram(name: string, value: number, labels: Record<string, string> = {}): void {
    const key = this.getMetricKey(name, labels);
    let histogram = this.histograms.get(key);
    
    if (!histogram) {
      histogram = { count: 0, sum: 0, buckets: new Map() };
      this.histograms.set(key, histogram);
    }
    
    histogram.count++;
    histogram.sum += value;
    
    // Record in buckets (.1, .5, 1, 2.5, 5, 10, +Inf)
    const buckets = [0.1, 0.5, 1, 2.5, 5, 10, Infinity];
    for (const bucket of buckets) {
      if (value <= bucket) {
        const current = histogram.buckets.get(bucket) || 0;
        histogram.buckets.set(bucket, current + 1);
      }
    }
  }

  getPrometheusFormat(): string {
    let output = '';
    
    // If no metrics yet, return at least some basic format
    if (this.counters.size === 0 && this.histograms.size === 0) {
      output += `# HELP sekiban_info Information about Sekiban application\n`;
      output += `# TYPE sekiban_info gauge\n`;
      output += `sekiban_info{version="1.0.0"} 1\n\n`;
    }
    
    // Output counters
    for (const [key, value] of this.counters.entries()) {
      const { name, labels } = this.parseMetricKey(key);
      output += `# HELP ${name} Total count of ${name}\n`;
      output += `# TYPE ${name} counter\n`;
      output += `${name}${this.formatLabels(labels)} ${value}\n\n`;
    }
    
    // Output histograms
    for (const [key, histogram] of this.histograms.entries()) {
      const { name, labels } = this.parseMetricKey(key);
      output += `# HELP ${name} Histogram of ${name}\n`;
      output += `# TYPE ${name} histogram\n`;
      
      // Buckets
      for (const [bucket, count] of histogram.buckets.entries()) {
        const bucketLabel = bucket === Infinity ? '+Inf' : bucket.toString();
        const bucketLabels = { ...labels, le: bucketLabel };
        output += `${name}_bucket${this.formatLabels(bucketLabels)} ${count}\n`;
      }
      
      output += `${name}_count${this.formatLabels(labels)} ${histogram.count}\n`;
      output += `${name}_sum${this.formatLabels(labels)} ${histogram.sum}\n\n`;
    }
    
    return output;
  }

  private getMetricKey(name: string, labels: Record<string, string>): string {
    const labelStr = Object.entries(labels)
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([k, v]) => `${k}="${v}"`)
      .join(',');
    return `${name}{${labelStr}}`;
  }

  private parseMetricKey(key: string): { name: string; labels: Record<string, string> } {
    const match = key.match(/^([^{]+)\{([^}]*)\}$/);
    if (!match) return { name: key, labels: {} };
    
    const [, name, labelStr] = match;
    const labels: Record<string, string> = {};
    
    if (labelStr) {
      const labelPairs = labelStr.split(',');
      for (const pair of labelPairs) {
        const [k, v] = pair.split('=');
        if (k && v) {
          labels[k] = v.replace(/"/g, '');
        }
      }
    }
    
    return { name, labels };
  }

  private formatLabels(labels: Record<string, string>): string {
    const entries = Object.entries(labels);
    if (entries.length === 0) return '';
    
    const formatted = entries
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([k, v]) => `${k}="${v}"`)
      .join(',');
    
    return `{${formatted}}`;
  }
}