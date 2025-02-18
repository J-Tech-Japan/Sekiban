﻿// See https://aka.ms/new-console-template for more information

using Sekiban.Pure.Postgres;

var builder = WebApplication.CreateBuilder(args);

// dotnet ef migrations add initial -p ../Sekiban.Infrastructure.Postgres



// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddNpgsql<SekibanDbContext>(builder.Configuration.GetConnectionString("DefaultConnection"));
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
}

app.UseHttpsRedirection();

app.Run();
