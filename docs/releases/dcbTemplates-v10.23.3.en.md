# DCB templates 10.23.3

The five starter templates for Sekiban DCB 10.23.3. Generated projects reference the 10.23.3 packages. The template code is unchanged from 10.23.2.

## Library fix included

All five templates register the Orleans materialized view (`AddSekibanDcbMaterializedViewOrleans()`), so generated projects get the 10.23.3 library fix: the materialized view grain keeps a stream notification after partial catch-up progress (issue #1339). See the 10.23.3 library release notes.

## Existing projects

Updating the template package does not change a project you already generated. To take the fix, change `SekibanDcbVersion` in `SekibanDcbTemplateVersion.props` to `10.23.3`.
