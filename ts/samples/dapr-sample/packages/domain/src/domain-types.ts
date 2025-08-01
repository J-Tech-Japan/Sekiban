import { createSchemaDomainTypes, globalRegistry, AggregateListProjector, SchemaMultiProjectorTypes, SchemaQueryTypes } from '@sekiban/core';
import { 
  CreateTask, 
  AssignTask, 
  CompleteTask, 
  UpdateTask, 
  DeleteTask,
  RevertTaskCompletion
} from './aggregates/task/commands/task-commands.js';
import {
  TaskCreated,
  TaskAssigned,
  TaskCompleted,
  TaskUpdated,
  TaskDeleted,
  TaskCompletionReverted
} from './aggregates/task/events/task-events.js';
import { taskProjectorDefinition, TaskProjector } from './aggregates/task/projectors/task-projector.js';
import { TaskListQuery, ActiveTaskListQuery, TasksByAssigneeQuery } from './aggregates/task/queries/index.js';
import { UserCreated, UserNameChanged, UserEmailChanged } from './aggregates/user/events/index.js';
import { userProjectorDefinition, UserProjector } from './aggregates/user/projectors/user-projector.js';
import { 
  InputWeatherForecast, 
  UpdateWeatherForecastLocation, 
  DeleteWeatherForecast 
} from './aggregates/weatherForecast/commands/weather-forecast-commands.js';
import {
  WeatherForecastInputted,
  WeatherForecastLocationUpdated,
  WeatherForecastDeleted
} from './aggregates/weatherForecast/events/weather-forecast-events.js';
import { weatherForecastProjectorDefinition, WeatherForecastProjector } from './aggregates/weatherForecast/projectors/weather-forecast-projector.js';
import { WeatherForecastQuery } from './aggregates/weatherForecast/queries/weather-forecast-query.js';

// Register all domain types with the global registry
// Events
globalRegistry.registerEvent(TaskCreated);
globalRegistry.registerEvent(TaskAssigned);
globalRegistry.registerEvent(TaskCompleted);
globalRegistry.registerEvent(TaskUpdated);
globalRegistry.registerEvent(TaskDeleted);
globalRegistry.registerEvent(TaskCompletionReverted);

// Commands
globalRegistry.registerCommand(CreateTask);
globalRegistry.registerCommand(AssignTask);
globalRegistry.registerCommand(CompleteTask);
globalRegistry.registerCommand(UpdateTask);
globalRegistry.registerCommand(DeleteTask);
globalRegistry.registerCommand(RevertTaskCompletion);

// User Events
globalRegistry.registerEvent(UserCreated);
globalRegistry.registerEvent(UserNameChanged);
globalRegistry.registerEvent(UserEmailChanged);

// WeatherForecast Events
globalRegistry.registerEvent(WeatherForecastInputted);
globalRegistry.registerEvent(WeatherForecastLocationUpdated);
globalRegistry.registerEvent(WeatherForecastDeleted);

// WeatherForecast Commands
globalRegistry.registerCommand(InputWeatherForecast);
globalRegistry.registerCommand(UpdateWeatherForecastLocation);
globalRegistry.registerCommand(DeleteWeatherForecast);

// Projectors
globalRegistry.registerProjector(taskProjectorDefinition);
globalRegistry.registerProjector(userProjectorDefinition);
globalRegistry.registerProjector(weatherForecastProjectorDefinition);

// TODO: Register queries when supported
// globalRegistry.registerQuery(GetTaskById);

export function createTaskDomainTypes() {
  const domainTypes = createSchemaDomainTypes(globalRegistry);
  
  // Register aggregate list projectors
  if (domainTypes.multiProjectorTypes && domainTypes.multiProjectorTypes instanceof SchemaMultiProjectorTypes) {
    // Register Task aggregate list projector
    domainTypes.multiProjectorTypes.registerAggregateListProjector(() => new TaskProjector());
    
    // Register User aggregate list projector
    domainTypes.multiProjectorTypes.registerAggregateListProjector(() => new UserProjector());
    
    // Register WeatherForecast aggregate list projector
    domainTypes.multiProjectorTypes.registerAggregateListProjector(() => new WeatherForecastProjector());
  }
  
  // Register queries
  if (domainTypes.queryTypes && domainTypes.queryTypes instanceof SchemaQueryTypes) {
    // Register Task queries
    domainTypes.queryTypes.registerQuery('TaskListQuery', TaskListQuery);
    domainTypes.queryTypes.registerQuery('ActiveTaskListQuery', ActiveTaskListQuery);
    domainTypes.queryTypes.registerQuery('TasksByAssigneeQuery', TasksByAssigneeQuery);
    
    // Register WeatherForecast queries
    domainTypes.queryTypes.registerQuery('WeatherForecastQuery', WeatherForecastQuery);
  }
  
  // Add convenience methods using the public API
  return {
    ...domainTypes,
    // Add helper methods using public API
    findCommandDefinition: (name: string) => globalRegistry.getCommand(name),
    findEventDefinition: (name: string) => globalRegistry.getEventDefinition(name),
    findProjectorDefinition: (name: string) => globalRegistry.getProjector(name)
  };
}