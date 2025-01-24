namespace comicdownloader

open System
open Microsoft.Azure.Functions.Worker
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open OneDrive

module TimedDownload =
    open Azure.Storage.Blobs
    [<Function("TimedDownload")>]
    let run ([<TimerTrigger("0 0 */5 * * *")>] myTimer: TimerInfo, context: FunctionContext) =
        let logger = context.GetLogger "TimedDownload"
        logger.LogInformation(sprintf "F# Time trigger function executed at: %A" DateTime.Now)
        logger.LogInformation(sprintf "Next timer schedule at: %A" myTimer.ScheduleStatus.Next)

        let blobClient = context.InstanceServices.GetRequiredService<BlobServiceClient>()
        
        Download.downloadImages logger blobClient


module TimedRefreshOneDriveToken =
    open Azure.Storage.Blobs
    [<Function("RefreshOneDrive")>]
    let run ([<TimerTrigger("0 0 0 * * *")>] myTimer: TimerInfo, context: FunctionContext) =
        let logger = context.GetLogger "Refresh Token"
        logger.LogInformation(sprintf "F# Time trigger function executed at: %A" DateTime.Now)
        logger.LogInformation(sprintf "Next timer schedule at: %A" myTimer.ScheduleStatus.Next)

        let blobClient = context.InstanceServices.GetRequiredService<BlobServiceClient>()

        task {
            match BlobClient.load<OneDriveSettings> "settings" "onedrive" blobClient with
            | Some settings -> 
                let! _, refresh = getAccessToken settings
                let settings = { settings with RefreshToken = refresh }
                BlobClient.store "settings" "onedrive" settings blobClient |> ignore
                ()
            | None -> 
                ()
        }
