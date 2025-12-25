# OrleansSekiban Playwright Tests

This project contains end-to-end tests for the OrleansSekiban application using Playwright.

## Prerequisites

- .NET 9.0 SDK
- Playwright browsers (installed via the `install-browsers.sh` script)

## Project Structure

- `Base/`: Contains the base test class that sets up the test environment
- `PageObjects/`: Contains page object models for interacting with the application's pages
- `Tests/`: Contains the actual test classes
- `Helpers/`: Contains helper classes and utilities

## Running the Tests

1. Install the Playwright browsers:

```bash
./install-browsers.sh
```

2. Run the tests:

```bash
./run-tests.sh
```

Or manually:

```bash
dotnet test
```

## Test Descriptions

### Weather Forecast Tests

The `WeatherForecastTests` class contains tests for the Weather Forecast functionality:

- `WeatherForecastsAreDisplayed`: Verifies that weather forecasts are displayed on the Weather page and checks that the
  forecasts contain locations, temperatures, and summaries.

## Screenshots

The tests take screenshots at various points for debugging purposes:

- `homepage.png`: The home page after initial navigation
- `weather-page.png`: The Weather page after navigation
- `weather-forecasts.png`: The Weather page showing the forecasts
- `final-state.png`: The final state of the page after the test
- `error.png`: The state of the page if an error occurs

## Troubleshooting

If the tests fail, check the following:

1. Make sure the application is running correctly
2. Check the screenshots for any UI issues
3. Check the console output for error messages
