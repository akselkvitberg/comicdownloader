module comicdownloader.Program

open System
open System.Collections.Generic
open System.Diagnostics
open System.Reflection
open System.Threading.Tasks
open Google.Cloud.Logging.Console
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open OpenTelemetry
open OpenTelemetry.Resources
open OpenTelemetry.Trace

let private defaultServiceName = "comicdownloader"

let private defaultServiceVersion =
    match Assembly.GetExecutingAssembly().GetName().Version with
    | null -> "unknown"
    | version -> version.ToString()

let private jobActivitySource = new ActivitySource($"{defaultServiceName}.jobs")

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

let private hasConfiguredOtlpEndpoint (configuration: Microsoft.Extensions.Configuration.ConfigurationManager) =
    [ "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"; "OTEL_EXPORTER_OTLP_ENDPOINT" ]
    |> List.exists (fun name ->
        match configuration[name] with
        | null -> false
        | value -> not (String.IsNullOrWhiteSpace(value)))

let private getServiceName (configuration: Microsoft.Extensions.Configuration.ConfigurationManager) =
    getSetting configuration [ "OTEL_SERVICE_NAME"; "K_SERVICE" ]
    |> Option.defaultValue defaultServiceName

let private getServiceVersion (configuration: Microsoft.Extensions.Configuration.ConfigurationManager) =
    getSetting configuration [ "OTEL_SERVICE_VERSION"; "K_REVISION" ]
    |> Option.defaultValue defaultServiceVersion

let private getCalvinAndHobbesSource (configuration: Microsoft.Extensions.Configuration.ConfigurationManager) =
    getSetting configuration [ "CALVIN_HOBBES_SOURCE_BUCKET" ]

let private runJob jobName operation (context: HttpContext) =
    task {
        let loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>()
        let storageContext = context.RequestServices.GetRequiredService<StorageContext.Context>()
        let logger = loggerFactory.CreateLogger(jobName)
        use activity = jobActivitySource.StartActivity($"{jobName}.run", ActivityKind.Internal)

        match activity with
        | null -> ()
        | currentActivity ->
            currentActivity.SetTag("job.name", jobName) |> ignore
            currentActivity.SetTag("job.trigger", "http") |> ignore

        try
            do! operation logger storageContext
            return Results.Ok({| job = jobName; status = "ok"; executedAt = DateTimeOffset.UtcNow |})
        with exn ->
            logger.LogError(exn, "Job {JobName} failed", jobName)
            return Results.Problem(title = $"{jobName} failed", detail = exn.Message, statusCode = 500)
    }

let builder = WebApplication.CreateBuilder([||])
let serviceName = getServiceName builder.Configuration
let serviceVersion = getServiceVersion builder.Configuration

builder.Logging.ClearProviders() |> ignore
builder.Logging.SetMinimumLevel(LogLevel.Information) |> ignore
builder.Logging.AddGoogleCloudConsole(fun options ->
    options.IncludeScopes <- true

    match getSetting builder.Configuration [ "GCP_PROJECT_ID"; "GOOGLE_CLOUD_PROJECT" ] with
    | Some projectId -> options.TraceGoogleCloudProjectId <- projectId
    | None -> ())
|> ignore

let openTelemetry =
    builder.Services
        .AddOpenTelemetry()
        .ConfigureResource(fun resource ->
            resource
                .AddService(serviceName = serviceName, serviceVersion = serviceVersion)
                .AddAttributes(
                    seq {
                        yield KeyValuePair<string, obj>("cloud.provider", box "gcp")

                        if not (String.IsNullOrWhiteSpace(builder.Environment.EnvironmentName)) then
                            yield KeyValuePair<string, obj>("deployment.environment.name", box builder.Environment.EnvironmentName)

                        match getSetting builder.Configuration [ "GCP_PROJECT_ID"; "GOOGLE_CLOUD_PROJECT" ] with
                        | Some projectId -> yield KeyValuePair<string, obj>("cloud.account.id", box projectId)
                        | None -> ()
                    })
            |> ignore)
        .WithTracing(fun tracing ->
            tracing
                .AddSource(jobActivitySource.Name)
                .AddAspNetCoreInstrumentation(fun options ->
                    options.RecordException <- true)
                .AddHttpClientInstrumentation()
            |> ignore)

if hasConfiguredOtlpEndpoint builder.Configuration then
    openTelemetry.UseOtlpExporter() |> ignore

builder.Services.AddSingleton<StorageContext.Context>(fun _ ->
    let configuration = builder.Configuration
    let storageContext: StorageContext.Context = {
        StorageClient = Google.Cloud.Storage.V1.StorageClient.Create()
        SecretManagerClient = Google.Cloud.SecretManager.V1.SecretManagerServiceClient.Create()
        ProjectId = requireSetting configuration "GCP project id" [ "GCP_PROJECT_ID"; "GOOGLE_CLOUD_PROJECT" ]
        BucketName = requireSetting configuration "GCS bucket name" [ "GCS_BUCKET_NAME" ]
        SecretPrefix = getSetting configuration [ "COMICDOWNLOADER_SECRET_PREFIX" ] |> Option.defaultValue "comicdownloader-"
        CalvinAndHobbesBucketName = getCalvinAndHobbesSource configuration
    }

    storageContext)
|> ignore

let app = builder.Build()

app.MapGet(
    "/health",
    Func<IResult>(fun () -> Results.Ok({| status = "ok"; service = serviceName |}))
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