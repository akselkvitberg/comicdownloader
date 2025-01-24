module comicdownloader.Download

open System.Net.Http
open System.Runtime.CompilerServices
open System.Security.Cryptography
open System.Xml.Linq
open System.Text.RegularExpressions
open System
open System.Text.Json
open Azure.Storage.Blobs
open SixLabors.ImageSharp
open SixLabors.ImageSharp.PixelFormats
open Microsoft.Extensions.Logging

let downloadImages (logger:ILogger) (blobClient :BlobServiceClient) =
    let httpClient = new HttpClient()

    logger.LogInformation "Loading settings"
        
    let vgCookie = BlobClient.load<string> "settings" "vgcookie" blobClient |> Option.defaultValue ""
    let oneDriveSettings = BlobClient.load<OneDrive.OneDriveSettings> "settings" "onedrive" blobClient
    let telegramSettings = BlobClient.load<Telegram.TelegramSettings> "settings" "telegram" blobClient

    let downloadUrl (urlTemplate:string) =
        let imageUrl = String.Format(urlTemplate, DateTime.Now.Year)
        httpClient.GetByteArrayAsync(imageUrl)

    let downloadRss (url:string) =
        task {
            let! xml = httpClient.GetStringAsync(url)
            let doc = XElement.Parse(xml)
            let innerXml = doc.Element("channel").Element("item").Element("description").Value
            let imageUrl = Regex.Match(innerXml, @"img.*src=""(\S+)""").Groups[1].Value

            return! httpClient.GetByteArrayAsync(imageUrl)
        }

    let downloadTU comic = 
        let timeString = DateTime.Now.ToString("yyyy-MM-dd")
        let data = $"https://www.tu.no/api/widgets/comics?name{comic}&date={timeString}"
        httpClient.GetByteArrayAsync(data)

    let downloadVg comic = 
            httpClient.DefaultRequestHeaders.Add("Cookie", [| "SP_ID=" + vgCookie|])
            httpClient.DefaultRequestHeaders.Add("User-Agent", [| "Mozilla/5.0 (Windows NT 10.0; WOW64; rv:44.0) Gecko/20100101 Firefox/44.0" |])
            let timeString = DateTime.Now.ToString("yyyy-MM-dd")

            httpClient.GetByteArrayAsync($"https://www.vg.no/tegneserier/api/images/{comic}/{timeString}")

    let downloadXkcd () = 
        task {
            let! result = httpClient.GetStringAsync("https://xkcd.com/info.0.json")
            let xkcdRss = JsonSerializer.Deserialize<{| img:string |}>(result)
            return! httpClient.GetByteArrayAsync(xkcdRss.img)
        }
        
        
    let getHash comicName (bytes:byte array) =
        let image = Image.Load<Rgba32>(bytes)
        let pixelBytes = Array.create<byte> (image.Width * image.Height * Unsafe.SizeOf<Rgba32>()) 0uy
        image.CopyPixelDataTo(pixelBytes)
        MD5.HashData(pixelBytes) |> Convert.ToBase64String

    let getFileName (bytes: byte array) = 
        let extension = 
            match bytes[0..4] with 
            | [|0xFFuy; 0xD8uy; _; _|] -> ".jpg"
            | [|0x42uy; 0x4Duy; _; _|] -> ".bmp"
            | [| 0x47uy; 0x49uy; 0x46uy |] -> ".gif"
            | _ -> ".png"
        DateTime.Now.ToString("yyyy.MM.dd") + extension

    let downloadComic comicName downloadFunction =
        task {
            try 
                let! bytes = downloadFunction
                let fileName = getFileName bytes
                let hash = getHash comicName bytes
                if BlobClient.exists comicName hash blobClient then
                    ()
                else
                    let! oneDriveResult = OneDrive.uploadFile oneDriveSettings comicName fileName bytes
                    let! telegram = Telegram.sendMessage telegramSettings bytes
                    let blob = BlobClient.storeBinary comicName hash bytes blobClient
                    ()
            with exn -> 
                logger.LogError(exn, "Could not download {ComicName}", comicName)
        }

    task {
        do! downloadComic "Pondus" (downloadVg "pondus")
        do! downloadComic "VG Gjesteserie" (downloadVg "gjesteserie")
        do! downloadComic "Hjalmar" (downloadVg "hjalmar")
        do! downloadComic "Lunch VG" (downloadVg "lunch")
        do! downloadComic "Tegnehanne" (downloadVg "hanneland")
        do! downloadComic "Storefri" (downloadVg "storefri")

        do! downloadComic "Lunch TU" (downloadTU "lunch")

        do! downloadComic "XKCD" (downloadXkcd ())

        do! downloadComic "Lunch E24" (downloadUrl "https://api.e24.no/content/v1/comics/{0:yyyy}-{0:MM}-{0:dd}")

        do! downloadComic "Calvin and Hobbes" (downloadRss "https://www.comicsrss.com/rss/calvinandhobbes.rss")
        do! downloadComic "Swords" (downloadRss "https://swordscomic.com/comic/feed/")
    }