# Deployment prerequisites

Before deploying the API, supply `Jwt__SecretKey` (at least 32 characters) from a secret store. Add an App Service Key Vault reference or a Container Apps secret reference to that environment variable. The shipped infrastructure does not provision this secret; startup fails without it.

Azure deployments default to Production. Sample users are created only in Development. To bootstrap the first administrator, also supply `Auth__InitialAdmin__Email` and `Auth__InitialAdmin__Password` from secrets. An existing account is never promoted by this setting. Identity tables and roles are initialized in every environment.
