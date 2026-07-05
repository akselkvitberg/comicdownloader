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

let downloadUrl (httpClient: HttpClient) (url: string) =
    task {
        try
            let! bytes = httpClient.GetByteArrayAsync(url)
            return Some bytes
        with ex ->
            printfn $"Failed to download {url}: {ex.Message}"
            return None
    }

let downloadTU (httpClient: HttpClient) (comic: string) =
    task {
        let url = $"https://www.tu.no/api/widgets/comics?name={comic}&date={DateTime.Now:``yyyy-MM-dd``}"
        let! bytes = downloadUrl httpClient url
        return bytes, None
    }

let downloadE24 (httpClient: HttpClient) =
    task {
        let url = $"https://api.e24.no/content/v1/comics/{DateTime.Now:``yyyy-MM-dd``}"
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

let downloadFarSide (httpClient: HttpClient) =
    task {
        let pageUrl = DateTime.Now.ToString("'https://www.thefarside.com/'yyyy'/'MM'/'dd")
        use request = new HttpRequestMessage(HttpMethod.Get, pageUrl)
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0")

        let! response = httpClient.SendAsync(request)
        response.EnsureSuccessStatusCode() |> ignore

        let! html = response.Content.ReadAsStringAsync()

        let comics =
            Regex.Match(
                html,
                "<div class=\"card tfs-comic js-comic\">.*?<img[^>]*data-src=\"(https://featureassets\\.amuniversal\\.com/assets/[^\"]+)\".*?<figcaption class=\"figure-caption\">(.*?)</figcaption>",
                RegexOptions.Singleline
            )

        if comics.Success then
            let imageUrl = comics.Groups[1].Value
            let caption = comics.Groups[2].Value.Trim() |> function | "" -> None | value -> Some value
            let! bytes = downloadUrl httpClient imageUrl
            return bytes, caption
        else
            return None, None
    }

let downloadSlackWyrm (httpClient: HttpClient) =
    task {
        let pageUrl = "https://joshuawright.net/"

        use request = new HttpRequestMessage(HttpMethod.Get, pageUrl)
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0")

        let! response = httpClient.SendAsync(request)
        response.EnsureSuccessStatusCode() |> ignore

        let! html = response.Content.ReadAsStringAsync()

        let comicImage =
            Regex.Match(
                html,
                "src=\"(images/picture%20-%20slackwyrm%20\\d+\\.[^\"?]+(?:\\?[^\"]*)?)\"",
                RegexOptions.IgnoreCase
            )

        if comicImage.Success then
            let imageUrl = Uri(Uri(pageUrl), comicImage.Groups[1].Value).AbsoluteUri
            let! bytes = downloadUrl httpClient imageUrl
            return bytes, None
        else
            return raise (InvalidOperationException($"No Slack Wyrm image URL was found at {pageUrl}"))
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

let downloadCalvinAndHobbes (storageContext: StorageContext.Context) =
    task {
        match storageContext.CalvinAndHobbesBucketName with
        | None -> return None, None
        | Some bucketName ->
            let date = DateTime.Now
            let part = date.Hour / 12 + 1
            match ObjectStore.loadBinary bucketName $"calvin_{date:yyyyMMdd}_{part}" storageContext with
            | Some bytes -> return Some bytes, None
            | None -> return None, None
    }

