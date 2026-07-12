using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Migration;
using Sekiban.Dcb.CosmosDb.TagMigration;

// The operator front-end for the destructive legacy tag-row migration.
//
// It contains no destructive logic of its own, and could not: the seam that expresses a tag-row delete is
// internal to Sekiban.Dcb.CosmosDb, so nothing outside that assembly can issue one. Everything below is
// argument parsing, file I/O, and printing — the deleting is done by CosmosDbLegacyTagMigrationService, the
// same one the service API exposes.
//
// The safety flow is the service's, not the CLI's:
//   1. `plan` reads, mutates nothing, and writes an artifact saying exactly which rows would die.
//   2. the operator reads it.
//   3. `apply` takes that artifact and refuses without --confirm and a backup file.
try
{
    return await Cli.RunAsync(args).ConfigureAwait(false);
}
catch (CosmosTagMigrationNotAuthorizedException ex)
{
    Console.Error.WriteLine($"REFUSED: {ex.Message}");
    return 2;
}
catch (CosmosTagMigrationPlanRejectedException ex)
{
    Console.Error.WriteLine($"REFUSED: {ex.Message}");
    return 3;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    Console.Error.WriteLine();
    Cli.PrintUsage(Console.Error);
    return 1;
}

namespace Sekiban.Dcb.CosmosDb.TagMigration
{
    /// <summary>
    ///     Argument parsing and printing. Every decision that matters — whether to delete, what to delete,
    ///     whether the plan is still valid — belongs to the service.
    /// </summary>
    internal static class Cli
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintUsage(Console.Out);
                return 0;
            }

            var command = args[0];
            var options = ParseOptions(args.Skip(1).ToArray());

            return command switch
            {
                "plan" => await PlanAsync(options).ConfigureAwait(false),
                "apply" => await ApplyAsync(options).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unknown command '{command}'.")
            };
        }

        private static async Task<CosmosDbLegacyTagMigrationService> BuildServiceAsync(CliOptions options)
        {
            var storeOptions = new CosmosDbEventStoreOptions();
            if (options.EventsContainer != null)
            {
                storeOptions.EventsContainerName = options.EventsContainer;
            }

            if (options.TagsContainer != null)
            {
                storeOptions.TagsContainerName = options.TagsContainer;
            }

            // The context owns the CosmosClient and lives for the length of the command.
#pragma warning disable CA2000
            var context = new CosmosDbContext(CliOptions.Required("--connection", options.ConnectionString), options.Database, null, storeOptions);
#pragma warning restore CA2000
            var factory = new CosmosDbLegacyTagMigrationServiceFactory(
                context,
                new DefaultCosmosContainerResolver(storeOptions));

            return await factory.CreateAsync(CliOptions.Required("--service-id", options.ServiceId)).ConfigureAwait(false);
        }

        private static async Task<int> PlanAsync(CliOptions options)
        {
            var service = await BuildServiceAsync(options).ConfigureAwait(false);

            var plan = await service.PlanAsync(new CosmosTagMigrationPlanOptions
            {
                FromSortableUniqueIdExclusive = options.From,
                ToSortableUniqueIdInclusive = options.To,
                MaxEventsToScan = options.MaxEvents ?? 10_000,
                MaxParallelism = options.Parallelism ?? 4,
                Checkpoint = options.Checkpoint
            }).ConfigureAwait(false);

            var path = options.PlanPath ?? "tag-migration-plan.json";
            await File.WriteAllTextAsync(path, plan.ToJson()).ConfigureAwait(false);

            Console.WriteLine($"Dry run. Nothing was changed.");
            Console.WriteLine($"  service      : {plan.ServiceId} ({plan.EventsContainer}/{plan.TagsContainer})");
            Console.WriteLine($"  scanned      : {plan.EventsScanned} event(s), {plan.KeysScanned} key(s)");
            Console.WriteLine($"  would reduce : {plan.Actions.Count} key(s)");
            Console.WriteLine($"  would delete : {plan.RowsToRemoveCount} row(s)");
            Console.WriteLine($"  left alone   : {plan.Skipped.Count} key(s) (corrupt or over the per-key cap)");
            Console.WriteLine($"  plan written : {path}");

            if (plan.HasMore)
            {
                Console.WriteLine();
                Console.WriteLine($"  The range was not fully scanned. Resume the next plan with:");
                Console.WriteLine($"    --checkpoint {plan.Checkpoint}");
            }

            if (plan.RowsToRemoveCount > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Read the plan before applying it. To delete the rows it lists:");
                Console.WriteLine($"  apply --plan {path} --backup <file> --confirm  (plus the same connection flags)");
            }

            return 0;
        }

        private static async Task<int> ApplyAsync(CliOptions options)
        {
            // Everything that can refuse, refuses BEFORE we so much as open a connection. A run that is not
            // authorized should not need a Cosmos account to be told so.
            var planPath = CliOptions.Required("--plan", options.PlanPath);
            var backupPath = CliOptions.Required("--backup", options.BackupPath);

            if (!File.Exists(planPath))
            {
                throw new CosmosTagMigrationPlanRejectedException(
                    $"The plan file '{planPath}' does not exist. A destructive run needs the artifact a dry " +
                    "run produced — run `plan` first and read it. Nothing was touched.");
            }

            var plan = CosmosTagMigrationPlan.FromJson(
                await File.ReadAllTextAsync(planPath).ConfigureAwait(false));

            if (!options.Confirm)
            {
                Console.Error.WriteLine(
                    $"REFUSED: this would delete {plan.RowsToRemoveCount} tag row(s) across " +
                    $"{plan.Actions.Count} key(s) in service '{plan.ServiceId}'. " +
                    "Pass --confirm to authorize it. Nothing was touched.");
                return 2;
            }

            var service = await BuildServiceAsync(options).ConfigureAwait(false);

            var report = await service.ApplyAsync(
                plan,
                new CosmosTagMigrationApplyOptions
                {
                    Confirm = true,
                    BackupWriter = new FileBackupWriter(backupPath),
                    MaxParallelism = options.Parallelism ?? 4
                }).ConfigureAwait(false);

            Console.WriteLine($"Applied. Backup written to {backupPath} before anything was deleted.");
            Console.WriteLine($"  reduced     : {report.Reduced} key(s)");
            Console.WriteLine($"  rows removed: {report.RowsRemoved}");
            Console.WriteLine($"  survivors   : {report.SurvivorsCreated} created");
            Console.WriteLine($"  lost races  : {report.LostRaces} (content changed mid-run; left alone)");
            Console.WriteLine($"  bad survivor: {report.StaleSurvivors} (canonical row missing/wrong; NOTHING deleted for these)");
            Console.WriteLine($"  stale       : {report.Stale} (the rows moved since the plan; re-plan)");
            Console.WriteLine($"  skipped     : {report.Skipped} (corrupt or over the cap; never touched)");

            return report.LostRaces > 0 || report.Stale > 0 || report.StaleSurvivors > 0 ? 4 : 0;
        }

        private static bool IsHelp(string argument) =>
            argument is "-h" or "--help" or "help";

        private static CliOptions ParseOptions(string[] args)
        {
            var options = new CliOptions();

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--connection":
                        options.ConnectionString = Next(args, ref index);
                        break;
                    case "--database":
                        options.Database = Next(args, ref index);
                        break;
                    case "--service-id":
                        options.ServiceId = Next(args, ref index);
                        break;
                    case "--events-container":
                        options.EventsContainer = Next(args, ref index);
                        break;
                    case "--tags-container":
                        options.TagsContainer = Next(args, ref index);
                        break;
                    case "--from":
                        options.From = Next(args, ref index);
                        break;
                    case "--to":
                        options.To = Next(args, ref index);
                        break;
                    case "--max-events":
                        options.MaxEvents = int.Parse(Next(args, ref index), null);
                        break;
                    case "--parallelism":
                        options.Parallelism = int.Parse(Next(args, ref index), null);
                        break;
                    case "--checkpoint":
                        options.Checkpoint = Next(args, ref index);
                        break;
                    case "--plan":
                        options.PlanPath = Next(args, ref index);
                        break;
                    case "--backup":
                        options.BackupPath = Next(args, ref index);
                        break;
                    case "--confirm":
                        options.Confirm = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option '{args[index]}'.");
                }
            }

            return options;
        }

        private static string Next(string[] args, ref int index)
        {
            index++;
            if (index >= args.Length)
            {
                throw new ArgumentException($"Option '{args[index - 1]}' needs a value.");
            }

            return args[index];
        }

        public static void PrintUsage(TextWriter writer)
        {
            writer.WriteLine("sekiban-dcb-tag-migration — reduce legacy Cosmos tag rows to their canonical row.");
            writer.WriteLine();
            writer.WriteLine("THIS TOOL DELETES DOCUMENTS. It is the only part of Sekiban that does.");
            writer.WriteLine("Migration is optional: legacy rows index their events perfectly well.");
            writer.WriteLine();
            writer.WriteLine("  plan   Read-only. Writes an artifact describing exactly which rows would be deleted.");
            writer.WriteLine("  apply  Destructive. Takes that artifact, and refuses without --confirm and --backup.");
            writer.WriteLine();
            writer.WriteLine("Connection (both commands):");
            writer.WriteLine("  --connection <cs>          Cosmos connection string (required)");
            writer.WriteLine("  --database <name>          Database name (default: SekibanDcb)");
            writer.WriteLine("  --service-id <id>          The ONE lineage to operate on (required)");
            writer.WriteLine("  --events-container <name>  Default: events");
            writer.WriteLine("  --tags-container <name>    Default: tags");
            writer.WriteLine();
            writer.WriteLine("plan:");
            writer.WriteLine("  --from <sortableUniqueId>  Scan events after this");
            writer.WriteLine("  --to <sortableUniqueId>    Scan events up to and including this");
            writer.WriteLine("  --max-events <n>           Bound one plan (default: 10000)");
            writer.WriteLine("  --parallelism <n>          Keys examined at once (default: 4)");
            writer.WriteLine("  --checkpoint <token>       Resume a bounded scan");
            writer.WriteLine("  --plan <file>              Where to write it (default: tag-migration-plan.json)");
            writer.WriteLine();
            writer.WriteLine("apply:");
            writer.WriteLine("  --plan <file>              The artifact from `plan` (required)");
            writer.WriteLine("  --backup <file>            Where the deleted rows are exported first (required)");
            writer.WriteLine("  --confirm                  Authorize the deletion. Without it, nothing is touched.");
        }
    }

    internal sealed class CliOptions
    {
        public string? ConnectionString { get; set; }
        public string Database { get; set; } = "SekibanDcb";
        public string? ServiceId { get; set; }
        public string? EventsContainer { get; set; }
        public string? TagsContainer { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public int? MaxEvents { get; set; }
        public int? Parallelism { get; set; }
        public string? Checkpoint { get; set; }
        public string? PlanPath { get; set; }
        public string? BackupPath { get; set; }
        public bool Confirm { get; set; }

        public static string Required(string name, string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException($"{name} is required.")
                : value;
    }
}
