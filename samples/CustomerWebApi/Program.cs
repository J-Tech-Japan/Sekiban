using ESSampleProjectDependency;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// プロジェクトの依存
Dependency.Register(builder.Services);
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
var executor = app.Services.GetService<AggregateCommandExecutor>();
executor!.ExecCreateCommandAsync<SnapshotManager, SnapshotManagerDto, CreateSnapshotManager>(new CreateSnapshotManager(SnapshotManager.SharedId))
    .Wait();
app.Run();
