module comicdownloader.Comics

open System
open System.Net.Http
open System.Net
open System.Net.Http.Json
open System.Text.Json
open System.Xml.Linq
open System.Text.RegularExpressions
open System.Globalization
open Microsoft.Extensions.Logging

// Failures propagate to downloadComic, which logs them as errors and alerts.
// Swallowing them here previously hid broken sources for weeks.
let downloadUrl (httpClient: HttpClient) (url: string) =
    task {
        try
            let! bytes = httpClient.GetByteArrayAsync(url)
            return Some bytes
        with ex ->
            return raise (HttpRequestException($"Failed to download {url}: {ex.Message}", ex))
    }

// tu.no and digi.no (Mentor Medier) list a comic's strips newest-first in a
// JSON-LD ItemList on /tegneserier/{comic}. The image server rescales to any
// requested width (upscaling too), so request the largest width the page itself
// offers in its srcset rather than the 1200px JSON-LD thumbnail.
let downloadMentorMedier (httpClient: HttpClient) (site: string) (comic: string) =
    task {
        let pageUrl = $"https://www.{site}/tegneserier/{comic}"

        use request = new HttpRequestMessage(HttpMethod.Get, pageUrl)
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0")

        let! response = httpClient.SendAsync(request)
        response.EnsureSuccessStatusCode() |> ignore

        let! html = response.Content.ReadAsStringAsync()

        let latestMatch =
            Regex.Match(
                html,
                $"\"url\":\"https://www\\.{Regex.Escape(site)}/tegneserie/{Regex.Escape(comic)}-[^\"]+\",\"image\":\"https://image\\.{Regex.Escape(site)}/(\\d+)\\.jpg"
            )

        if not latestMatch.Success then
            return raise (InvalidOperationException($"No comic image was found at {pageUrl}"))
        else
            let imageId = latestMatch.Groups[1].Value

            let width =
                Regex.Matches(html, $"imageId={imageId}&[^\"]*?&width=(\\d+)")
                |> Seq.map (fun m -> int m.Groups[1].Value)
                |> Seq.fold max 1200

            let imageUrl = $"https://image.{site}/{imageId}.jpg?imageId={imageId}&width={width}"
            let! bytes = downloadUrl httpClient imageUrl
            return bytes, None
    }

// e24.no embeds Lunch from Strand Comics. A future date returns the latest strip,
// so an early run just re-downloads yesterday's and is deduplicated by hash.
let downloadE24 (httpClient: HttpClient) =
    task {
        let url = $"https://api.strandcomics.no/striper/e24/lunch/{DateTime.Now:``yyyy-MM-dd``}"
        let! bytes = downloadUrl httpClient url
        return bytes, None
    }

type XkcdResponse = { img: string; alt: string }
let downloadXkcd (httpClient: HttpClient) =
    task {
        let! result = httpClient.GetFromJsonAsync<XkcdResponse> "https://xkcd.com/info.0.json"
        let xkcdRss = Option.ofObj result

        match xkcdRss with
        | Some result when not (String.IsNullOrWhiteSpace result.img) ->
            let! bytes = downloadUrl httpClient result.img
            return bytes, Some result.alt
        | _ -> return None, None
    }

let downloadRss (httpClient: HttpClient) (url: string) =
    task {
       let! xml = httpClient.GetStringAsync(url)
       let doc = XElement.Parse(xml)
       let item = doc.Element("channel") |> Option.ofObj |> Option.bind (fun channel -> channel.Element("item") |> Option.ofObj)
       let description = item |> Option.bind (fun value -> value.Element("description") |> Option.ofObj)

       let descriptionValue =
           match description with
           | Some value -> value
           | None -> raise (InvalidOperationException($"No RSS description was found at {url}"))

       let innerXml = descriptionValue.Value
       let imageUrl = Regex.Match(innerXml, @"img.*src=""(\S+)""").Groups[1].Value

       if String.IsNullOrWhiteSpace(imageUrl) then
           raise (InvalidOperationException($"No image URL was found in RSS description at {url}"))

       let! bytes = downloadUrl httpClient imageUrl
       return bytes, None
   }

// Extracts the relative comic image paths from the Slack Wyrm landing page,
// preserving page order and dropping the duplicate <img> tags the page emits.
// A single strip can be published as multiple parts whose filenames carry a
// trailing letter (e.g. "slackwyrm 1572a.jpg", "slackwyrm 1572b.jpg"), so the
// number may be followed by an optional letter suffix.
let extractSlackWyrmImageUrls (html: string) =
    Regex.Matches(
        html,
        "src=\"(images/picture%20-%20slackwyrm%20\\d+[a-z]?\\.[^\"?]+(?:\\?[^\"]*)?)\"",
        RegexOptions.IgnoreCase
    )
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Seq.distinct
    |> Seq.toList

let downloadSlackWyrm (httpClient: HttpClient) =
    task {
        let pageUrl = "https://joshuawright.net/"

        use request = new HttpRequestMessage(HttpMethod.Get, pageUrl)
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0")

        let! response = httpClient.SendAsync(request)
        response.EnsureSuccessStatusCode() |> ignore

        let! html = response.Content.ReadAsStringAsync()

        match extractSlackWyrmImageUrls html with
        | [] -> return raise (InvalidOperationException($"No Slack Wyrm image URL was found at {pageUrl}"))
        | relativeUrls ->
            let images = ResizeArray<byte array * string option>()

            for relativeUrl in relativeUrls do
                let imageUrl = Uri(Uri(pageUrl), relativeUrl).AbsoluteUri
                let! bytes = downloadUrl httpClient imageUrl

                match bytes with
                | Some bytes -> images.Add(bytes, None)
                | None -> ()

            return List.ofSeq images
    }

let downloadAbsurdgalleriet (httpClient: HttpClient) =
    task {
        let indexUrl = "https://www.abcnyheter.no/tegneserie"

        use indexRequest = new HttpRequestMessage(HttpMethod.Get, indexUrl)
        indexRequest.Headers.UserAgent.ParseAdd("Mozilla/5.0")

        let! indexResponse = httpClient.SendAsync(indexRequest)
        indexResponse.EnsureSuccessStatusCode() |> ignore

        let! indexHtml = indexResponse.Content.ReadAsStringAsync()

        // The index lists comics newest-first; the first article link is today's comic.
        let latestMatch =
            Regex.Match(indexHtml, "href=\"(https://www\\.abcnyheter\\.no/tegneserie/[^\"/]+/\\d+)\"")

        if not latestMatch.Success then
            return raise (InvalidOperationException($"No comic link was found at {indexUrl}"))
        else
            let comicUrl = latestMatch.Groups[1].Value

            use comicRequest = new HttpRequestMessage(HttpMethod.Get, comicUrl)
            comicRequest.Headers.UserAgent.ParseAdd("Mozilla/5.0")

            let! comicResponse = httpClient.SendAsync(comicRequest)
            comicResponse.EnsureSuccessStatusCode() |> ignore

            let! comicHtml = comicResponse.Content.ReadAsStringAsync()

            let imageMatch =
                Regex.Match(comicHtml, "<meta[^>]*property=\"og:image\"[^>]*content=\"([^\"]+)\"")

            if not imageMatch.Success then
                return raise (InvalidOperationException($"No image URL was found at {comicUrl}"))
            else
                let imageUrl = WebUtility.HtmlDecode(imageMatch.Groups[1].Value)

                // The full comic text lives in the single-line <h2>. The og:title masks the
                // punchline word with "***", so it must not be used as the caption.
                let captionMatch =
                    Regex.Match(comicHtml, "<h2[^>]*\\bsingleline\\b[^>]*>(.*?)</h2", RegexOptions.Singleline)

                let caption =
                    if captionMatch.Success then
                        match WebUtility.HtmlDecode(captionMatch.Groups[1].Value).Trim() with
                        | "" -> None
                        | value -> Some value
                    else
                        None

                let! bytes = downloadUrl httpClient imageUrl
                return bytes, caption
    }

let downloadComicsKingdom (httpClient: HttpClient) (comic: string) =
    task {
        let date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        let pageUrl = $"https://comicskingdom.com/{comic}/{date}"

        use request = new HttpRequestMessage(HttpMethod.Get, pageUrl)
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0")

        let! response = httpClient.SendAsync(request)
        response.EnsureSuccessStatusCode() |> ignore

        let! html = response.Content.ReadAsStringAsync()
        let pattern =
            sprintf
                "title=%s&amp;url=%%2F%s%%2F%s&amp;img=(https%%3A%%2F%%2F[^\"' ]+)"
                (Regex.Escape(date))
                (Regex.Escape(comic))
                (Regex.Escape(date))

        let imageUrlMatch = Regex.Match(html, pattern)

        if imageUrlMatch.Success then
            let imageUrl = Uri.UnescapeDataString(imageUrlMatch.Groups[1].Value)
            let! bytes = downloadUrl httpClient imageUrl
            return bytes, None
        else
            return None, None
    }

// Pre-scheduled strips live in the private source bucket as {prefix}_{yyyyMMdd}_{part},
// two per day: part 1 before noon, part 2 after.
let downloadFromSourceBucket (storageContext: StorageContext.Context) (prefix: string) =
    task {
        match storageContext.CalvinAndHobbesBucketName with
        | None -> return None, None
        | Some bucketName ->
            let date = DateTime.Now
            let part = date.Hour / 12 + 1
            match ObjectStore.loadBinary bucketName $"{prefix}_{date:yyyyMMdd}_{part}" storageContext with
            | Some bytes -> return Some bytes, None
            | None -> return None, None
    }

