module fsCustomer.WebAPI.Program
#nowarn "20"
open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Sekiban.Core.Dependency
open Sekiban.Infrastructure.Cosmos
open Sekiban.Web.OpenApi.Extensions
open fsCustomer.Dependency
open Sekiban.Web.Dependency;


let builder = WebApplication.CreateBuilder(Environment.GetCommandLineArgs())
builder.AddSekibanWithDependency<FsCustomerDependency>();
builder.AddSekibanCosmosDb()
builder.AddSekibanWebFromDomainDependency<FsCustomerDependency>()
builder.Services.AddSwaggerGen(fun options -> options.ConfigureForSekibanWeb())

builder.Services.AddControllers()

let app = builder.Build()

if app.Environment.IsDevelopment() then
    app.UseDeveloperExceptionPage()
    app.UseSwagger()
    app.UseSwaggerUI(fun c -> c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1")) |> ignore

app.UseHttpsRedirection()

app.UseAuthorization()
app.MapControllers()

app.Run()
