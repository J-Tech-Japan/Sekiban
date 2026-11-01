# Sekiban DCB 10.23.4

- Version: `10.23.4`. 26 DCB library packages. Targets net9.0 and net10.0. `Microsoft.Orleans.*` 10.3.1 (unchanged).
- No data or schema migration is required, and no public API changed.

## Fixes

- **Cancelling during tag reservation no longer leaves reservations active.** When a command was cancelled while its tag reservations were in flight, successful reservations stayed active until they expired (30 seconds with the default options), and other writers of those tags were refused. Successful reservations are now cancelled. This applies to `ExecuteAsync` and to serialized commits. If a cancel call itself fails, that reservation still stays active until it expires.
- **Failed reservation calls are now logged with Orleans** (issue #1349). When a cancel, confirm or notify call to the tag-consistent actor threw, or the actor could not be resolved, the failure was reported at most with `Debug.WriteLine`, which Release builds remove. With Orleans these failures are now warnings from the logger category `Sekiban.Dcb.Actors.TagReservationHelper`, with the operation, the tag and the reservation code when there is one, if the host provides an `ILoggerFactory`. A call that returns `false` is not logged. The result of the command does not change.

## Improvements

- **PostgreSQL: less work while tag-head locks are held** (issue #1347, in part). An append with expected tag positions no longer creates EF Core automatic savepoints. A failed append still writes no events.
- **Serialized commits request tag reservations concurrently**, as `ExecuteAsync` already did.

## Check before upgrading

- A cancelled command now waits for its in-flight reservation requests to finish, so that it can cancel them. If a reservation request is slow, the cancellation returns later.
- That cancellation now reports `OperationCanceledException`. In `ExecuteAsync` it was `TaskCanceledException`, which derives from it. Update code that matches only `TaskCanceledException`.
- If a reservation is refused while the cancellation is in progress, `ExecuteAsync` and serialized commits return the existing `InvalidOperationException` ("Failed to reserve tags") instead.
- Other actor accessors, such as in-memory, no longer write these failures with `Debug.WriteLine` and have no replacement log.
- If you alert on warnings, you may see the new warnings from the category above.

## Templates

Templates for 10.23.4 are released separately, after the libraries.
