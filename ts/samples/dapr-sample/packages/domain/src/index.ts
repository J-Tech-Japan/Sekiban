// Import domain types to ensure registration happens
import './domain-types.js';

// Export domain types
export { createTaskDomainTypes } from './domain-types.js';

// Export commands
export { 
  CreateTask, 
  AssignTask, 
  CompleteTask, 
  UpdateTask, 
  DeleteTask,
  RevertTaskCompletion
} from './aggregates/task/commands/task-commands.js';

// Export events
export {
  TaskCreated,
  TaskAssigned,
  TaskCompleted,
  TaskUpdated,
  TaskDeleted,
  TaskCompletionReverted
} from './aggregates/task/events/task-events.js';

// Export projector and states
export { TaskProjector } from './aggregates/task/projectors/task-projector.js';
export type { TaskState, CompletedTaskState, TaskPayloadUnion } from './aggregates/task/projectors/task-projector.js';

// Export multi-projector
export { TaskMultiProjector } from './aggregates/task/projectors/task-multi-projector.js';

// Export queries
export { GetTaskById } from './aggregates/task/queries/task-queries.js';
export { GetAllTasks } from './aggregates/task/queries/get-all-tasks.js';
export { TaskListQuery, ActiveTaskListQuery, TasksByAssigneeQuery } from './aggregates/task/queries/index.js';
export type { TaskListResponse } from './aggregates/task/queries/index.js';

// Export User aggregate
export * from './aggregates/user/index.js';

// Export WeatherForecast commands
export { 
  InputWeatherForecast, 
  UpdateWeatherForecastLocation, 
  DeleteWeatherForecast,
  RemoveWeatherForecast
} from './aggregates/weatherForecast/commands/weather-forecast-commands.js';

// Export WeatherForecast events
export {
  WeatherForecastInputted,
  WeatherForecastLocationUpdated,
  WeatherForecastDeleted
} from './aggregates/weatherForecast/events/weather-forecast-events.js';

// Export WeatherForecast projector and states
export { WeatherForecastProjector } from './aggregates/weatherForecast/projectors/weather-forecast-projector.js';
export type { WeatherForecastState, DeletedWeatherForecastState, WeatherForecastPayloadUnion } from './aggregates/weatherForecast/payloads/weather-forecast-payloads.js';

// Export WeatherForecast queries
export { WeatherForecastQuery } from './aggregates/weatherForecast/queries/weather-forecast-query.js';

// Export value objects
export { TemperatureCelsiusSchema, createTemperatureCelsius, toFahrenheit } from './valueObjects/temperature-celsius.js';
export type { TemperatureCelsius } from './valueObjects/temperature-celsius.js';

// Export actor interfaces
export * from './actors/interfaces/index.js';