# DCB templates 10.24.3

The five starter templates for Sekiban DCB 10.24.3. Generated projects reference the 10.24.3 packages and Orleans 10.4.2. These notes cover the changes since templates 10.24.2.

## Library changes included

- 10.24.3: materialized-view initialization of one database is serialized, so two initializers at the same time no longer stop the host ([#1362](https://github.com/J-Tech-Japan/Sekiban/issues/1362)); `InMemoryEventStore` returns an empty tag state for a tag with no events ([#1340](https://github.com/J-Tech-Japan/Sekiban/issues/1340)); on PostgreSQL, an append whose events carry two or more tags updates the tag heads with a fixed number of set-based statements ([#1347](https://github.com/J-Tech-Japan/Sekiban/issues/1347)). The public API and the dependencies are unchanged since 10.24.2. See the 10.24.3 library release notes.

## Template changes

- The templates reference Sekiban DCB 10.24.3. Nothing else in the generated projects changed.

## Existing projects

Updating the template package does not change a project you already generated. To take the library changes, set `SekibanDcbVersion` to `10.24.3` in `SekibanDcbTemplateVersion.props`. No other change is needed.
