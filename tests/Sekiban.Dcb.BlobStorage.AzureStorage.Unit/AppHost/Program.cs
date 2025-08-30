using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Azurite emulator for Azure Storage (generic container)
builder.AddContainer("storage", "mcr.microsoft.com/azure-storage/azurite")
       .WithImageTag("latest")
        // port, targetPort, name
       .WithEndpoint(10000, 10000, "blob")
       .WithEndpoint(10001, 10001, "queue")
       .WithEndpoint(10002, 10002, "table");

// Nothing else to host; emulator is the resource under test
builder.Build().Run();
