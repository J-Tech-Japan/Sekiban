# DCB template verification runbook

Manual, full-feature verification of the five `Sekiban.Dcb.Templates` templates. It generates each app, starts it
with Aspire, and exercises every API route, the CLI, persistence across a restart, the AWS snapshot offload, and the
web frontends.

Unit tests and the template validation workflow check that the generated code builds and passes its tests. This
runbook checks that the running app behaves correctly.

Nothing in this directory is packed into the template package; only `content/**` is.

## When to run

- Before every template release: on the release candidate commit, with the library version the templates will reference.
- Monthly, on the current release branch (`main` for 10.x, `dcb/v11` for 11.x).
- After a change to template code that a unit test cannot cover: endpoints, AppHost wiring, auth, frontends, the CLI.

A full pass of all five templates takes about half a day. The Decider API script alone takes about 25 minutes.

## Layout

| path | used for |
|---|---|
| `common/launch.sh` | Start an AppHost in the background (Development, http profile) and record the pid and the containers that already existed. |
| `common/wait-healthy.sh` | Wait until the ApiService `/health` answers Healthy. |
| `common/stop.sh` | Stop the AppHost and wait until its processes and the containers it created are gone. |
| `common/collect-logs.sh` | Copy the ApiService / Web / WebNext output that Aspire captured, with a count of `fail:` / `crit:` lines. Run it before `stop.sh`. |
| `common/process-env.sh` | Print an environment variable of a running process, for example the event store connection string the AppHost gave the ApiService. |
| `common/exercise-cli.sh` | Every CLI command against the Postgres event store (Orleans, WithoutResult, Decider templates), with exit-code checks. |
| `common/exercise-cli-aws.sh` | The CLI of the AWS templates (only `list`). |
| `common/offload-check.sh`, `common/bulk-forecast.sh` | AWS templates: grow a projection snapshot past the DynamoDB item limit and check that it is offloaded to S3. |
| `samples/exercise-api.sh`, `samples/persist-check.sh` | API and restart checks for `sekiban-dcb-orleans`, `sekiban-dcb-orleans-withoutresult`, `sekiban-dcb-orleans-aws`. |
| `decider/exercise-api.sh`, `decider/persist-check.sh` | API and restart checks for `sekiban-dcb-decider`, `sekiban-dcb-decider-aws`. |

The scripts are bash. They run on macOS and Linux, and use `curl -q` so that a personal `~/.curlrc` does not change them.

## Prerequisites

- Docker, running and healthy. The AppHosts start Postgres, Azurite or LocalStack, and admin tools as containers.
- The .NET 10 SDK.
- Node.js 20 or later and npm, for the Decider templates' Next.js frontend (`WebNext`). The AppHost runs `npm install`
  and `next dev` itself.
- `python3`, `uuidgen`, `curl` and `docker` on the `PATH`.
- The Aspire CLI bundle (DCP and dashboard). Restoring an AppHost puts it into the NuGet cache
  (`aspire.cli.<rid>`), and `common/launch.sh` finds it there. Set `ASPIRE_BUNDLE_DIR` to use another one.
- Free ports: 15258 (dashboard), 19266, 20220, 5010 / 5141 (ApiService), 5234 / 5180 (Blazor Web), 3000 (WebNext),
  4566 (LocalStack), 11111 and 30000 (Orleans).
- Optional: Playwright or another browser automation tool, for the UI checks.

## 1. Generate the apps

First check that no data volumes of an earlier run are left. Each generated app gets a new Postgres password, and a
volume left by an earlier app generated at the same path can make the new app fail to log in to Postgres or start with
old data:

```bash
docker volume ls --format '{{.Name}}' | grep -E '^v(orleans|withoutresult|decider)\.apphost-'   # expect no output
```

Then generate from a local pack of the branch under test:

```bash
V=$PWD/templates/Sekiban.Dcb.Templates/verification   # run from the repository root
W=$HOME/dcb-verification; mkdir -p $W/pkg $W/apps $W/run
dotnet pack templates/Sekiban.Dcb.Templates/Sekiban.Dcb.Templates.csproj -c Release -o $W/pkg -p:PackageVersion=0.0.0-verify
dotnet new install $W/pkg/Sekiban.Dcb.Templates.0.0.0-verify.nupkg --debug:custom-hive $W/hive
cd $W/apps
dotnet new sekiban-dcb-orleans               -n VOrleans          -o VOrleans          --debug:custom-hive $W/hive
dotnet new sekiban-dcb-orleans-withoutresult -n VWithoutResult    -o VWithoutResult    --debug:custom-hive $W/hive
dotnet new sekiban-dcb-orleans-aws           -n VWithoutResultAws -o VWithoutResultAws --debug:custom-hive $W/hive
dotnet new sekiban-dcb-decider               -n VDecider          -o VDecider          --debug:custom-hive $W/hive
dotnet new sekiban-dcb-decider-aws           -n VDeciderAws       -o VDeciderAws       --debug:custom-hive $W/hive
for a in V*; do dotnet build $a/$a.slnx && dotnet test $a/$a.slnx --no-build || echo "FAILED: $a"; done
```

To verify a published version instead, install `Sekiban.Dcb.Templates::<version>` from nuget.org into the custom hive
and generate in the same way.

The template's library version must already be on nuget.org (or in a local feed added to the generated app's
`nuget.config`). Keep the generated apps outside this repository so that the repository's `Directory.Build.props` does
not apply to them.

Expected: every app builds with 0 warnings and 0 errors, and every test passes.

## 2. Run one template

The same steps apply to each template. Run one app at a time: the apps share ports and container names.

```bash
A=$W/apps/VDecider; R=$W/run/decider; mkdir -p $R
$V/common/launch.sh $A/VDecider.AppHost $R/apphost.log          # writes $R/apphost.log.pid
$V/common/wait-healthy.sh http://localhost:5141

# API (copy the script first if you might edit it during the run: bash reads scripts while it runs them)
$V/decider/exercise-api.sh http://localhost:5141 $R/api full; echo "API FAILS=$?"

# CLI against the app's own event store
CONN=$($V/common/process-env.sh 'VDecider.ApiService/bin/Debug/net10.0/VDecider.ApiService$' ConnectionStrings__DcbPostgres)
eval "$(tr ' ' '\n' < $R/api.ids | grep -E '^[A-Z0-9]+=' | sed 's/^/export /')"
$V/common/exercise-cli.sh $A/VDecider.Cli/bin/Debug/net10.0/VDecider.Cli "$CONN" $R/cli $SID $FID $Q1 > $R/cli.log; echo "CLI FAILS=$?"

# UI: see "UI checklist" below

$V/common/collect-logs.sh $R/run1
$V/common/stop.sh $R/apphost.log.pid

# Persistence: start again and read back what the first run wrote
$V/common/launch.sh $A/VDecider.AppHost $R/apphost2.log && $V/common/wait-healthy.sh http://localhost:5141
$V/decider/persist-check.sh http://localhost:5141 $R/api.ids; echo "PERSIST FAILS=$?"
$V/common/collect-logs.sh $R/run2
$V/common/stop.sh $R/apphost2.log.pid
```

Always pass the connection explicitly to the CLI (`-d` with `-c` or `--sqlite-path`, as `exercise-cli.sh` does). A CLI started without it
falls back to its profile or user secrets, which can point at a real database.

### Per template

| template | app dir | ApiService | Web (Blazor) | API script | CLI script | notes |
|---|---|---|---|---|---|---|
| `sekiban-dcb-orleans` | `VOrleans` | `http://localhost:5010` | 5234 | `TEMP_FORMAT=object samples/exercise-api.sh` | `common/exercise-cli.sh <cli> <conn> <dir> $SID $FID` | ResultBox flavour; `temperatureC` is `{"value":n}`, clamped to -50..50 |
| `sekiban-dcb-orleans-withoutresult` | `VWithoutResult` | `http://localhost:5141` | 5234 | `samples/exercise-api.sh` | same as above | |
| `sekiban-dcb-orleans-aws` | `VWithoutResultAws` | `http://localhost:5141` | 5234 | `samples/exercise-api.sh` | `common/exercise-cli-aws.sh <cli>` | DynamoDB + S3 on LocalStack; run `offload-check.sh` |
| `sekiban-dcb-decider` | `VDecider` | `http://localhost:5141` | 5180 | `decider/exercise-api.sh` | `common/exercise-cli.sh <cli> <conn> <dir> $SID $FID $Q1` | auth, meeting rooms, WebNext on 3000 |
| `sekiban-dcb-decider-aws` | `VDeciderAws` | `http://localhost:5141` | 5234 | `decider/exercise-api.sh` | `common/exercise-cli-aws.sh <cli>` | Decider on LocalStack; run `offload-check.sh` |

The dashboard (`http://localhost:15258`) lists every resource with its URL. Check it when a port differs.

The Decider templates seed `user1@example.com`, `user2@example.com`, `user3@example.com` (role User) and
`admin@example.com` (role Admin), all with the password `Sekiban1234%`, in Development only.

### Storage variants (non-AWS templates)

Repeat the run with environment variables set before `launch.sh`:

- `Orleans__UseInMemoryStreams=false`: Orleans streams on Azure Queues (Azurite). Run the API script in `full` mode.
- `Sekiban__Database=sqlite`: SQLite event store (`events.db` in the ApiService directory). Run the API script in
  `subset` mode, run the CLI script with `sqlite:<path to events.db>` as the connection, restart once with the same
  setting and run the persist check, then delete the `events.db*` files.

### AWS templates: snapshot offload

With the app running (after the API script):

```bash
L=$(docker ps --format '{{.Names}}' | grep '^localstack-' | head -1)
$V/common/offload-check.sh http://localhost:5141 $L vwithoutresultaws 2500   # prefix = lower-cased app name
```

Expected: `OFFLOAD OK` (exit 0). The `WeatherForecastProjection` row in `<prefix>-events-projections` shows
`isOffloaded: true`, and `<prefix>-snapshots` holds the snapshot object (about 0.7 MB for 2500 forecasts). The
script repeats the persist call for up to about 90 seconds, because events newer than the safe window (about
20 seconds) are not in the persisted snapshot yet.

## UI checklist

By hand or with Playwright, with the app running. Every page must load without an error overlay or a failing request
in the browser console.

- Blazor Web (all templates): home, counter, weather (add, edit location, remove), students (add), classrooms (add),
  enrollments (enroll, drop).
- WebNext (Decider templates, `http://localhost:3000`): register a user and log in; meeting rooms (admin: create and
  edit; user: no create button); reservations (quick reservation, draft, hold, cancel, confirm as admin); approvals
  (admin: approve and reject, pending count updates); users (admin: monthly limit); weather, students, classrooms,
  enrollments. Leave a page open for over 15 minutes to see that the access token refreshes without a logout.

## Reading the results

- `<prefix>.tsv`: one line per check: `PASS` / `FAIL`, name, request, expected and actual status, body excerpt.
  `<prefix>.log` has the full request and response of each check.
- The API, CLI and persist scripts exit with the number of failed checks (at most 255), and `offload-check.sh` exits
  0 or 1. The count is also printed (`FAILS=`, `CLI FAILS=`, `PERSIST FAILS=`).
  **A correct app gives 0 for each**, except the persist check of the AWS templates (see below). Any FAIL is a
  defect in the template or the library, unless the expectation in the script is out of date; in that case fix the
  script in the same pull request as the behaviour change.
- `ADMIN_RELOGINS` (Decider) counts how often the script logged the admin in again because the cookie disappeared
  from curl's cookie jar. It is informational: it has been seen with curl 8.7 in some long runs and not in others,
  and could not be reproduced with single requests.
- `collect-logs.sh` prints the number of `fail:` / `crit:` lines per log. In the Decider templates, `fail:` lines
  for the `auth-initialization` health check during the first seconds of startup are normal (the identity database
  is still being created); anything else needs a look.
- `stop.sh` must end with `stopped after ...`. It ignores Aspire DCP processes that were already running before
  `launch.sh`, but it counts any container created during the run, so do not start other containers meanwhile.

### Expected behaviour that looks like a failure

- AWS templates keep no data across an AppHost restart: LocalStack community does not persist DynamoDB. Their persist
  check fails by design; run it with `TRIES=3` to finish quickly. The non-AWS templates keep Postgres and Azurite data
  in named volumes, so their persist check must pass.
- Decider: a rotated refresh token still works for 60 seconds and returns the successor token, so that two frontend
  instances refreshing at once do not log the user out. The script checks both sides of the grace period.
- Decider: holding a draft in an approval room without an `approvalRequestId` starts the approval flow (200).
- Decider: room create/update and the test-data routes are admin-only (403 for a regular user).
- `GET /api/mv/students?waitForSortableUniqueId=<future id>` waits and answers 504; that is the intended timeout.

## Cleanup

```bash
docker ps -a        # no containers of the app left
docker volume ls    # the non-AWS templates keep named data volumes (<app>.apphost-<hash>-...); remove them when done:
docker volume ls --format '{{.Name}}' | grep -E '^v(orleans|withoutresult|decider)\.apphost-' | xargs docker volume rm
dotnet new uninstall Sekiban.Dcb.Templates --debug:custom-hive $W/hive
rm -rf $W
```
