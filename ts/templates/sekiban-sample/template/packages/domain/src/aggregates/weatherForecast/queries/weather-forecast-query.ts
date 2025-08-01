import { 
  IMultiProjectionListQuery,
  AggregateListProjector
} from '@sekiban/core';
import { WeatherForecastProjector } from '../projectors/weather-forecast-projector.js';
import { type WeatherForecastState } from '../payloads/weather-forecast-payloads.js';
import { toFahrenheit } from '../../../valueObjects/temperature-celsius.js';

export interface WeatherForecastResponse {
  weatherForecastId: string;
  location: string;
  date: string;
  temperatureC: number;
  summary?: string;
  temperatureF: number;
}

/**
 * Query to get weather forecasts (excluding deleted ones)
 */
export class WeatherForecastQuery implements IMultiProjectionListQuery<
  AggregateListProjector<WeatherForecastProjector>,
  WeatherForecastQuery,
  WeatherForecastResponse
> {
  /**
   * Get the aggregate type for this query
   */
  getAggregateType(): string {
    return 'WeatherForecast';
  }
  
  /**
   * Get the projector for this query
   */
  getProjector() {
    return new WeatherForecastProjector();
  }
  
  /**
   * Get the multi-projector name for this query
   */
  getMultiProjectorName(): string {
    return AggregateListProjector.getMultiProjectorName(() => new WeatherForecastProjector());
  }
  
  /**
   * Filter out deleted weather forecasts
   */
  handleFilter(aggregate: any): boolean {
    // The aggregate structure from the response shows the payload directly contains aggregateType
    const payload = aggregate.payload;
    
    // Only include items with aggregateType === 'WeatherForecast'
    // This excludes items with aggregateType === 'DeletedWeatherForecast'
    return (
      payload && 
      typeof payload === 'object' && 
      'aggregateType' in payload &&
      payload.aggregateType === 'WeatherForecast'
    );
  }
  
  /**
   * Sort weather forecasts by date
   */
  handleSort(a: any, b: any): number {
    const payloadA = a.payload as WeatherForecastState;
    const payloadB = b.payload as WeatherForecastState;
    return payloadA.date.localeCompare(payloadB.date);
  }
  
  /**
   * Transform aggregate to response format
   */
  transformToResponse(aggregate: any): WeatherForecastResponse {
    const payload = aggregate.payload as WeatherForecastState;
    return {
      weatherForecastId: aggregate.aggregateId,
      location: payload.location,
      date: payload.date,
      temperatureC: payload.temperatureC.value,
      summary: payload.summary,
      temperatureF: toFahrenheit(payload.temperatureC)
    };
  }
}