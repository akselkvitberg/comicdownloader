module comicdownloader.Program

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

let private getSetting (configuration: Microsoft.Extensions.Configuration.ConfigurationManager) names =
    names
    |> List.tryPick (fun name ->
        match configuration[name] with
        | null -> None
        | value when String.IsNullOrWhiteSpace(value) -> None
        | value -> Some value)

let private requireSetting configuration label names =
    match getSetting configuration names with
    | Some value -> value
    | None -> invalidOp $"Missing configuration setting: {label}"

let private runJob jobName operation (context: HttpContext) =
    task {
        let loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>()
        let storageContext = context.RequestServices.GetRequiredService<StorageContext.Context>()
        let logger = loggerFactory.CreateLogger(jobName)

        try
            do! operation logger storageContext
            return Results.Ok({| job = jobName; status = "ok"; executedAt = DateTimeOffset.UtcNow |}) :> IResult
        with exn ->
            logger.LogError(exn, "Job {JobName} failed", jobName)
            return Results.Problem(title = $"{jobName} failed", detail = exn.Message, statusCode = 500) :> IResult
    }

let builder = WebApplication.CreateBuilder([||])

builder.Services.AddSingleton<StorageContext.Context>(fun _ ->
    let configuration = builder.Configuration
    let storageContext: StorageContext.Context = {
        StorageClient = Google.Cloud.Storage.V1.StorageClient.Create()
        SecretManagerClient = Google.Cloud.SecretManager.V1.SecretManagerServiceClient.Create()
        ProjectId = requireSetting configuration "GCP project id" [ "GCP_PROJECT_ID"; "GOOGLE_CLOUD_PROJECT" ]
        BucketName = requireSetting configuration "GCS bucket name" [ "GCS_BUCKET_NAME" ]
        SecretPrefix = getSetting configuration [ "COMICDOWNLOADER_SECRET_PREFIX" ] |> Option.defaultValue "comicdownloader-"
    }

    storageContext)
|> ignore

let app = builder.Build()

app.MapGet(
    "/health",
    Func<IResult>(fun () -> Results.Ok({| status = "ok"; service = "comicdownloader" |}) :> IResult)
)
|> ignore

app.MapPost("/jobs/download", Func<HttpContext, Task<IResult>>(runJob "download" TimedDownload.runDownload))
|> ignore

app.MapPost(
    "/jobs/refresh-onedrive",
    Func<HttpContext, Task<IResult>>(runJob "refresh-onedrive" TimedDownload.runRefreshOneDriveToken)
)
|> ignore

app.Run()