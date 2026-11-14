# DCB templates 10.25.0

The five starter templates for Sekiban DCB 10.25.0. Generated projects reference the 10.25.0 packages and Orleans 10.4.2, and still target net10.0. These notes cover the changes since templates 10.24.3.

## Library changes included

- 10.25.0: the library packages target net10.0 and net11.0, and no longer contain net9.0 assets. The net10.0 and net11.0 assets use the same dependency versions as 10.24.3. The public API and the behaviour are unchanged since 10.24.3. See the 10.25.0 library release notes.

## Template changes

- The templates reference Sekiban DCB 10.25.0. Generated projects keep targeting net10.0; nothing else in them changed.

## Existing projects

Updating the template package does not change a project you already generated. To take the library changes, set `SekibanDcbVersion` to `10.25.0` in `SekibanDcbTemplateVersion.props`. No other change is needed for a project that targets net10.0.
