module comicdownloader.Download

open System.Net.Http
open System.Net
open System.Runtime.CompilerServices
open System.Security.Cryptography
open System.Xml.Linq
open System.Text.RegularExpressions
open System
open System.Text.Json
open SixLabors.ImageSharp
open SixLabors.ImageSharp.PixelFormats
open Microsoft.Extensions.Logging

let private formatDownloadError comicName (exn: exn) =
    let message = $"Comic download failed for {comicName}: {exn.Message}"

    if message.Length > 4000 then
        message.Substring(0, 4000)
    else
        message

let downloadImages (logger: ILogger) (storageContext: StorageContext.Context) =
    let httpClient = new HttpClient()

    logger.LogInformation "Loading settings"

    let vgCookie =
        BlobClient.load<string> "settings" "vgcookie" storageContext
        |> Option.defaultValue ""

    let oneDriveSettings =
        BlobClient.load<OneDrive.OneDriveSettings> "settings" "onedrive" storageContext

    let telegramSettings =
        BlobClient.load<Telegram.TelegramSettings> "settings" "telegram" storageContext

    logger.LogInformation(
        "Settings loaded. OneDrive configured: {HasOneDriveSettings}. Telegram configured: {HasTelegramSettings}. VG cookie configured: {HasVgCookie}",
        Option.isSome oneDriveSettings,
        Option.isSome telegramSettings,
        not (String.IsNullOrWhiteSpace(vgCookie))
    )

    let downloadUrl (urlTemplate: string) =
        let imageUrl = String.Format(urlTemplate, DateTime.Now.Year)
        httpClient.GetByteArrayAsync(imageUrl)

    let downloadRss (url: string) =
        task {
            let! xml = httpClient.GetStringAsync(url)
            let doc = XElement.Parse(xml)
            let innerXml = doc.Element("channel").Element("item").Element("description").Value
            let imageUrl = Regex.Match(innerXml, @"img.*src=""(\S+)""").Groups[1].Value

            return! httpClient.GetByteArrayAsync(imageUrl)
        }

    let downloadFarSide () =
        task {
            let pageUrl = DateTime.Now.ToString("'https://www.thefarside.com/'yyyy'/'MM'/'dd")
            use request = new HttpRequestMessage(HttpMethod.Get, pageUrl)
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0")

            let! response = httpClient.SendAsync(request)
            response.EnsureSuccessStatusCode() |> ignore

            let! html = response.Content.ReadAsStringAsync()

            let comics =
                Regex.Matches(
                    html,
                    "<div class=\"card tfs-comic js-comic\">.*?<img[^>]*data-src=\"(https://featureassets\\.amuniversal\\.com/assets/[^\"]+)\".*?<figcaption class=\"figure-caption\">(.*?)</figcaption>",
                    RegexOptions.Singleline
                )
                |> Seq.cast<Match>
                |> Seq.toList

            let selectedComic =
                comics
                |> List.tryPick (fun comic ->
                    let caption =
                        comic.Groups[2].Value
                        |> fun value -> Regex.Replace(value, "<.*?>", "")
                        |> WebUtility.HtmlDecode
                        |> fun value -> value.Trim()

                    if String.IsNullOrWhiteSpace(caption) then
                        None
                    else
                        Some(comic.Groups[1].Value, Some caption))

            let imageUrl, caption =
                match selectedComic with
                | Some comic -> comic
                | None ->
                    match comics |> List.tryHead with
                    | Some comic -> comic.Groups[1].Value, None
                    | None -> failwith $"Could not find The Far Side comic image on {pageUrl}"

            let! bytes = httpClient.GetByteArrayAsync(imageUrl)
            return bytes, caption
        }

    let downloadTU comic =
        let timeString = DateTime.Now.ToString("yyyy-MM-dd")
        let data = $"https://www.tu.no/api/widgets/comics?name{comic}&date={timeString}"
        httpClient.GetByteArrayAsync(data)

    let downloadVg comic =
        httpClient.DefaultRequestHeaders.Add("Cookie", [| "SP_ID=" + vgCookie |])

        httpClient.DefaultRequestHeaders.Add(
            "User-Agent",
            [| "Mozilla/5.0 (Windows NT 10.0; WOW64; rv:44.0) Gecko/20100101 Firefox/44.0" |]
        )

        let timeString = DateTime.Now.ToString("yyyy-MM-dd")

        httpClient.GetByteArrayAsync($"https://www.vg.no/tegneserier/api/images/{comic}/{timeString}")

    let downloadXkcd () =
        task {
            let! result = httpClient.GetStringAsync("https://xkcd.com/info.0.json")
            let xkcdRss = JsonSerializer.Deserialize<{| img: string; alt: string |}>(result)
            let! bytes = httpClient.GetByteArrayAsync(xkcdRss.img)
            return bytes, Some xkcdRss.alt
        }


    let getHash comicName (bytes: byte array) =
        let image = Image.Load<Rgba32>(bytes)

        let pixelBytes =
            Array.create<byte> (image.Width * image.Height * Unsafe.SizeOf<Rgba32>()) 0uy

        image.CopyPixelDataTo(pixelBytes)
        MD5.HashData(pixelBytes) |> Convert.ToBase64String

    let getFileName (bytes: byte array) =
        let extension =
            match bytes[0..4] with
            | [| 0xFFuy; 0xD8uy; _; _ |] -> ".jpg"
            | [| 0x42uy; 0x4Duy; _; _ |] -> ".bmp"
            | [| 0x47uy; 0x49uy; 0x46uy |] -> ".gif"
            | _ -> ".png"

        DateTime.Now.ToString("yyyy.MM.dd") + extension

    let downloadComicWithCaption comicName (downloadFunction: Threading.Tasks.Task<byte array * string option>) =
        task {
            try
                logger.LogInformation("Starting comic download for {ComicName}", [| box comicName |])
                let! bytes, telegramCaption = downloadFunction
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

                    let! telegramResult = Telegram.sendMessage logger telegramSettings bytes telegramCaption

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

    let downloadComic comicName (downloadFunction: Threading.Tasks.Task<byte array>) =
        downloadComicWithCaption comicName (
            task {
                let! bytes = downloadFunction
                return bytes, None
            }
        )

    task {
        logger.LogInformation("Starting comic download batch")
        // do! downloadComic "Pondus" (downloadVg "pondus")
        // do! downloadComic "VG Gjesteserie" (downloadVg "gjesteserie")
        // do! downloadComic "Hjalmar" (downloadVg "hjalmar")
        // do! downloadComic "Lunch VG" (downloadVg "lunch")
        // do! downloadComic "Tegnehanne" (downloadVg "hanneland")
        // do! downloadComic "Storefri" (downloadVg "storefri")
        // do! downloadComic "Pappa" (downloadVg "pappa")

        do! downloadComic "Lunch TU" (downloadTU "lunch")

        do! downloadComicWithCaption "XKCD" (downloadXkcd ())

        do! downloadComic "Lunch E24" (downloadUrl "https://api.e24.no/content/v1/comics/{0:yyyy}-{0:MM}-{0:dd}")

        //do! downloadComic "Calvin and Hobbes" (downloadRss "https://www.comicsrss.com/rss/calvinandhobbes.rss")
        do! downloadComicWithCaption "The Far Side" (downloadFarSide ())
        do! downloadComic "Swords" (downloadRss "https://swordscomic.com/comic/feed/")
        logger.LogInformation("Comic download batch completed")
    }
