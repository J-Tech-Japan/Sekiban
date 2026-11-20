# DCB templates 10.25.1

The five starter templates for Sekiban DCB 10.25.1. Generated projects reference the 10.25.1 packages and Orleans 10.4.2, and still target net10.0. These notes cover the changes since templates 10.25.0.

## Library changes included

- 10.25.1: Unsafe Window materialized views no longer stop at a row whose nullable column is null (the weather forecast view in every template, for a forecast without a summary); on SQL Server, null binary values and date and time values are bound with their column types. The DynamoDB checkpoint store saves every conditional checkpoint write, not only the first one (the two AWS templates). The `*/event-statistics` routes in every template count deliveries again, instead of always returning 0. The public API and the dependencies are unchanged since 10.25.0. See the 10.25.1 library release notes.

## Template changes

- The templates reference Sekiban DCB 10.25.1. Nothing else in the generated projects changed.

## Existing projects

Updating the template package does not change a project you already generated. To take the library changes, set `SekibanDcbVersion` to `10.25.1` in `SekibanDcbTemplateVersion.props`. No other change is needed.
