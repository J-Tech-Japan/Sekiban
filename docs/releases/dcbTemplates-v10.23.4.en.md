# DCB templates 10.23.4

The five starter templates for Sekiban DCB 10.23.4. Generated projects reference the 10.23.4 packages. The template code is unchanged from 10.23.3.

## Library changes included

Generated projects get the 10.23.4 library changes: cancelling during tag reservation no longer leaves reservations active, and failed reservation calls are logged as warnings with Orleans. See the 10.23.4 library release notes, including the checks before upgrading.

## Existing projects

Updating the template package does not change a project you already generated. To take the changes, change `SekibanDcbVersion` in `SekibanDcbTemplateVersion.props` to `10.23.4`.
