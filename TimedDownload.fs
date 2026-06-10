namespace comicdownloader

open System
open Microsoft.Extensions.Logging
open OneDrive

module TimedDownload =
    let downloadSchedule = "0 0 */5 * * *"
    let refreshSchedule = "0 0 0 * * *"

    let runDownload (logger: ILogger) (storageContext: StorageContext.Context) =
        task {
            logger.LogInformation("Download job executed at: {Timestamp}", DateTimeOffset.UtcNow)
            do! Download.downloadImages logger storageContext
        }

    let runRefreshOneDriveToken (logger: ILogger) (storageContext: StorageContext.Context) =
        task {
            logger.LogInformation("Refresh token job executed at: {Timestamp}", DateTimeOffset.UtcNow)

            match BlobClient.load<OneDriveSettings> "settings" "onedrive" storageContext with
            | Some settings ->
                logger.LogInformation("Loaded OneDrive settings for token refresh")
                let! _, refresh = getAccessToken logger settings
                let settings = { settings with RefreshToken = refresh }
                BlobClient.store "settings" "onedrive" settings storageContext |> ignore
                logger.LogInformation("Stored refreshed OneDrive refresh token")
            | None ->
                logger.LogWarning("Skipping OneDrive refresh because no settings were found")
        }
