# DCB templates 10.23.2

The five starter templates for Sekiban DCB 10.23.2. Generated projects reference the 10.23.2 packages.

## Reservation sample: rule bypasses fixed (Decider templates)

The meeting-room reservation sample in the Decider templates let callers get around its own rules. The 10.22.0 and 10.23.1 template packages contain these problems (there was no 10.23.0 template package); earlier versions were not checked. They are in the sample application code, not in the Sekiban libraries. **Updating the template package does not change a project you already generated: if you started from a Decider template, apply the same fixes to your project.** The changes are in https://github.com/J-Tech-Japan/Sekiban/pull/1330.

- **Decider.Aws: the organizer was taken from the request body.** `POST /api/reservations/draft` kept the body's `organizerId`, and `POST /api/reservations/quick` made up an id when the user id claim was invalid. The command handler decides the administrator exemption, the month window and the monthly limit from that id, so a signed-in user could send an administrator's id to bypass the rules, or create a reservation in another user's name. Now the organizer id and name come only from the signed-in user's claims, and a request without a valid user id claim gets 401, as in the Decider template.
- **Decider: quick reservations skipped the monthly limit.** `POST /api/reservations/quick`, the path the Next.js front-end uses by default, checked only the month window, for everyone. A user at the monthly limit could keep reserving, and an administrator was held to the window. Quick reservations and drafts now apply the same rules through one shared helper (`ReservationCreationRules`).
- **Decider: benchmark switches removed.** Setting the environment variable `SEKIBAN_BENCHMARK_SKIP_USER_RESERVATION_RULES` to `true` made the command handler skip the reservation rules in any environment. The setting `Benchmark:AllowDebugUserHeaders` (on in the Development settings file, not tied to the environment in code) let an administrator create reservations as any user through the `X-Debug-User-Id` header, or through the request body when the administrator's own user id claim was missing or invalid. Both are removed, together with `BENCHMARK_PROFILE` and the unused diagnostics pass-through in the AppHost.

## Other changes

- **Health checks say what they check.** In all five templates `/health` includes an Orleans check that is healthy only while the local silo is active. Before, it was healthy whenever the process was up, and the AWS templates had no Orleans check. In the Decider templates `/health` also waits until the start-up routine for the authentication database has finished: the Identity tables and the roles exist, and the routine has attempted to create the sample users (Development) or the configured initial administrator. The AppHost starts the front-ends after that, so in a normal local start the sample users exist before the first login. A user that cannot be created, for example an initial administrator whose password Identity rejects, is logged and does not keep `/health` unhealthy; check the log. If the database cannot be reached or a role cannot be created after the retries, `/health` stays unhealthy until the instance is restarted. The shipped Azure Container Apps and ECS definitions probe `/health` and restart the instance; the shipped App Service definitions configure no health probe, and a local run is not restarted. There is no guaranteed upper bound on the time until `/health` is healthy.
- **Diagnostic routes removed.** Base, WithoutResult and WithoutResult.Aws no longer map `/api/orleans/test` and `/api/health`, which were available in every environment without authentication. Use `/health` and `/alive`.
- **Rejected weather commands return 400** in the Decider templates (for example deleting a forecast twice returned 500).
- **A project named `App` builds.** Avoid C# type names such as `Program` as the project name.
- **Blazor pages** show the product name and say what the sample is; in the Decider templates the home page points to the Next.js front-end for the meeting-room application.

## Not covered

The templates do not enable cold events. Real cloud deployments, the shipped infrastructure's health probes and multi-silo behaviour were not verified for this release.
