module comicdownloader.Download

open System.Net.Http
open System.Runtime.CompilerServices
open System.Security.Cryptography
open System
open System.Threading.Tasks
open SixLabors.ImageSharp
open SixLabors.ImageSharp.PixelFormats
open Microsoft.Extensions.Logging
open Comics

let private getFileName (bytes: byte array) =
    let extension =
        match bytes[0..4] with
        | [| 0xFFuy; 0xD8uy; _; _ |] -> ".jpg"
        | [| 0x42uy; 0x4Duy; _; _ |] -> ".bmp"
        | [| 0x47uy; 0x49uy; 0x46uy |] -> ".gif"
        | _ -> ".png"

    DateTime.Now.ToString("yyyy.MM.dd") + extension

let private getHash comicName (bytes: byte array) =
    let image = Image.Load<Rgba32>(bytes)

    let pixelBytes =
        Array.create<byte> (image.Width * image.Height * Unsafe.SizeOf<Rgba32>()) 0uy

    image.CopyPixelDataTo(pixelBytes)
    MD5.HashData(pixelBytes) |> Convert.ToBase64String

let private formatDownloadError comicName (exn: exn) =
    let message = $"Comic download failed for {comicName}: {exn.Message}"

    if message.Length > 4000 then
        message.Substring(0, 4000)
    else
        message

let downloadImages (logger: ILogger) (storageContext: StorageContext.Context) =
    let httpClient = new HttpClient()
    
    logger.LogInformation "Loading settings"

    let oneDriveSettings =
        BlobClient.load<OneDrive.OneDriveSettings> "settings" "onedrive" storageContext

    let telegramSettings =
        BlobClient.load<Telegram.TelegramSettings> "settings" "telegram" storageContext

    logger.LogInformation(
        "Settings loaded. OneDrive configured: {HasOneDriveSettings}. Telegram configured: {HasTelegramSettings}.",
        Option.isSome oneDriveSettings,
        Option.isSome telegramSettings
    )

    let downloadComic comicName (downloadFunction: Task<byte array option * string option>) =
        task {
            try
                logger.LogInformation("Starting comic download for {ComicName}", [| box comicName |])
                let! (bytes, caption) = downloadFunction

                match bytes with
                | None ->
                    logger.LogInformation("Skipping {ComicName} because the source returned no image", [| box comicName |])
                | Some bytes ->

                    logger.LogInformation("Downloaded {ByteCount} bytes for {ComicName}", [| box bytes.Length; box comicName |])
                    let fileName = getFileName bytes
                    let hash = getHash comicName bytes

                    if BlobClient.exists comicName hash storageContext then
                        logger.LogInformation("Skipping {ComicName} because hash {Hash} already exists", [| box comicName; box hash |])
                        ()
                    else
                        logger.LogInformation("New comic detected for {ComicName}; writing filename {FileName} with hash {Hash}", [| box comicName; box fileName; box hash |])

                        let! oneDriveResult = OneDrive.uploadFile logger oneDriveSettings comicName fileName bytes

                        match oneDriveResult with
                        | Ok _ -> logger.LogInformation("OneDrive upload succeeded for {ComicName}", [| box comicName |])
                        | Error error -> logger.LogWarning(error, "OneDrive upload returned an error for {ComicName}", [| box comicName |])

                        let! telegramResult = Telegram.sendMessage logger telegramSettings bytes caption

                        match telegramResult with
                        | Ok _ -> logger.LogInformation("Telegram image send succeeded for {ComicName}", [| box comicName |])
                        | Error error -> logger.LogWarning(error, "Telegram image send returned an error for {ComicName}", [| box comicName |])

                        BlobClient.storeBinary comicName hash bytes storageContext |> ignore
                        logger.LogInformation("Stored {ComicName} bytes in object storage under hash {Hash}", [| box comicName; box hash |])
                        ()
            with exn ->
                logger.LogError(exn, "Could not download {ComicName}", comicName)

                let! _ =
                    Telegram.sendText logger telegramSettings (formatDownloadError comicName exn)

                ()
        }

    task {
        logger.LogInformation("Starting comic download batch")
        do! downloadComic "Lunch TU" (downloadTU httpClient "lunch")
        do! downloadComic "Dunce" (downloadTU httpClient "dunce")

        do! downloadComic "XKCD" (downloadXkcd httpClient)

        do! downloadComic "Lunch E24" (downloadE24 httpClient)

        do! downloadComic "Calvin and Hobbes" (downloadCalvinAndHobbes storageContext)

        do! downloadComic "The Far Side" (downloadFarSide httpClient)

        do! downloadComic "Swords" (downloadRss httpClient "https://swordscomic.com/comic/feed/")
        
        logger.LogInformation("Comic download batch completed")
    }
