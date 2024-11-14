var builder = DistributedApplication.CreateBuilder(args);

// var postgres = builder.AddPostgresContainer("aspirePostgres").WithPgAdmin().AddDatabase("SekibanAspirePostgres");

var postgresPassword = builder.AddParameter("postgres-password",secret:true);
var postgres = builder.AddPostgres("aspirePostgres",password: postgresPassword)
    .WithDataVolume("aspirePostgresData")
    .WithPgAdmin().AddDatabase("SekibanAspirePostgres");

var apiServiceWithPostgres = builder
    .AddProject<Projects.AspireAndSekibanSample_ApiService_Postgres>("apiservicepostgres")
    .WithReference(postgres);

// Use AddConnectionString for already existing CosmosDB
//var cosmos = builder.AddConnectionString("SekibanAspireCosmos");

// Use AddAzureCosmosDB for CosmosDB Emulator
var cosmos = builder.AddAzureCosmosDB("SekibanAspireCosmos").AddDatabase("SekibanDb").RunAsEmulator(
    e => e
        // two lines needs to include for .WithDataVolume
        //.WithVolume("cosmosAspireData", "/tmp/cosmos/appdata") // Save data to a volume so it persists between runs
        //.WithEnvironment("AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE", "true") // Enable data persistence in the emulator

        .WithEnvironment("AZURE_COSMOS_EMULATOR_PARTITION_COUNT", "3") // Speed up the emulator by using only 3 partitions

    // two lines needs to include for default "RunAsEmulator"
    .WithEnvironment("AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE", "127.0.0.1") // Without this option, we couldn't connect to the emulator from the host.
    .WithEndpoint(port: 8081, name: "explorer", targetPort: 8081) // Expose the emulator's data explorer. Without this, we couldn't access the cosmos db from the client.   
    //.WithEndpoint(port: 10251, name: "cosmos1", targetPort: 10251)
    //.WithEndpoint(port: 10252, name: "cosmos2", targetPort: 10252)
    //.WithEndpoint(port: 10253, name: "cosmos3", targetPort: 10253)
    //.WithEndpoint(port: 10254, name: "cosmos4", targetPort: 10254)
    //.WithEndpoint(port: 10255, name: "cosmos5", targetPort: 10255)
    );
 
var blob = builder.AddConnectionString("SekibanAspireBlob");

var apiService = builder.AddProject<Projects.AspireAndSekibanSample_ApiService>("apiservice")
    .WithReference(cosmos)
    .WithReference(blob);


builder.AddProject<Projects.AspireAndSekibanSample_Web>("webfrontend")
    .WithReference(apiService)
    .WithReference(apiServiceWithPostgres);

builder.Build().Run();
