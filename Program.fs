module comicdownloader.Program

open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Azure.Storage.Blobs

let builder =
    FunctionsApplication.CreateBuilder([||])
    
builder
    .ConfigureFunctionsWebApplication()
    .ConfigureTablesExtension()
    .ConfigureBlobStorageExtension()
    |> ignore

builder.Services.AddScoped<BlobServiceClient>(fun _ -> BlobServiceClient(builder.Configuration["AzureWebJobsStorage"])) |> ignore

builder.Build().Run()