using System.Diagnostics;
using System.Text.Json;
using Dcb.Domain;
using Dcb.Domain.Student;
using Microsoft.EntityFrameworkCore;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.ServiceId;
using Testcontainers.PostgreSql;

var label = args.FirstOrDefault() ?? "branch";
var seconds = args.Length > 1 ? int.Parse(args[1]) : 5;
await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
await postgres.StartAsync();
var factory = new Factory(postgres.GetConnectionString());
await using (var db = factory.CreateDbContext()) await db.Database.MigrateAsync();
var eventTypes = DomainType.GetDomainTypes().EventTypes;
var cases = new (string Name, int Writers, int Private, bool Shared)[]
{
    ("1 tag", 1, 1, false), ("5 tags", 1, 5, false), ("20 tags", 1, 20, false),
    ("shared only", 16, 0, true), ("shared+4", 16, 4, true), ("disjoint 5", 16, 5, false)
};
Console.WriteLine($"{label}: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; CPUs={Environment.ProcessorCount}; runtime={Environment.Version}; seconds={seconds}");
foreach (var scenario in cases)
{
    var measured = new List<Run>();
    for (var run = 0; run <= 5; run++)
    {
        await using (var db = factory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("TRUNCATE dcb_events, dcb_tags, dcb_tag_heads, dcb_tag_head_violations RESTART IDENTITY CASCADE");
        var stores = Enumerable.Range(0, scenario.Writers).Select(_ => new PostgresEventStore(factory, eventTypes, new FixedServiceIdProvider("benchmark"))).ToArray();
        var tags = Enumerable.Range(0, scenario.Writers).Select(w =>
            Enumerable.Range(0, scenario.Private).Select(i => $"private:{w}:{i}").Concat(scenario.Shared ? ["shared"] : Array.Empty<string>()).ToList()).ToArray();
        long position = 0;
        Event Next(int writer) => new(new StudentCreated(Guid.CreateVersion7(), "benchmark"),
            Interlocked.Increment(ref position).ToString("D20"), nameof(StudentCreated), Guid.CreateVersion7(), new EventMetadata("c", "r", "benchmark"), tags[writer]);
        // Ordinary typed append -> TagHeadWriter.TypedBatch, no expectations. Create fixed heads outside timing.
        for (var w = 0; w < scenario.Writers; w++)
        {
            var seed = await stores[w].WriteEventsAsync([Next(w)]);
            if (!seed.IsSuccess) throw seed.GetException();
        }
        var timer = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, scenario.Writers).Select(async w =>
        {
            var latencies = new List<double>();
            while (timer.Elapsed.TotalSeconds < seconds)
            {
                var start = Stopwatch.GetTimestamp();
                var result = await stores[w].WriteEventsAsync([Next(w)]);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (!result.IsSuccess) throw result.GetException();
                latencies.Add(elapsed); // Only successful writes contribute to throughput and latency.
            }
            return latencies;
        }).ToArray();
        var samples = (await Task.WhenAll(tasks)).SelectMany(x => x).Order().ToArray();
        timer.Stop();
        var resultRun = new Run(samples.Length / timer.Elapsed.TotalSeconds, samples[(int)((samples.Length - 1) * .5)], samples[(int)((samples.Length - 1) * .99)], samples.Length);
        Console.WriteLine(JsonSerializer.Serialize(new { label, scenario = scenario.Name, run, warmup = run == 0, result = resultRun }));
        if (run > 0) measured.Add(resultRun);
    }
    Console.WriteLine(JsonSerializer.Serialize(new { label, scenario = scenario.Name, summary = new {
        throughput = measured.Select(r => r.Throughput).Order().ElementAt(2),
        p50 = measured.Select(r => r.P50).Order().ElementAt(2), p99 = measured.Select(r => r.P99).Order().ElementAt(2),
        successfulWrites = measured.Select(r => r.Successes).ToArray() } }));
}
record Run(double Throughput, double P50, double P99, int Successes);
sealed class Factory(string connection) : IDbContextFactory<SekibanDcbDbContext>
{
    public SekibanDcbDbContext CreateDbContext() => new(new DbContextOptionsBuilder<SekibanDcbDbContext>().UseNpgsql(connection).Options);
}
