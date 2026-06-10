open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Diagnostics
open System.Text
open System.Text.Json
open System.Text.Json.Nodes

let client = new HttpClient()
let clientId = "87424810-3904-4ad6-8e6a-07ec846f7353"
let redirectUri = "http://localhost:8000"
let listenerPrefix = "http://localhost:8000/"
let scopes = [ "Files.ReadWrite.AppFolder"; "offline_access" ]

type OneDriveSettings = {
    ClientId: string
    RefreshToken: string
}

let getAuthorizationUrl () =
    let encodedScope = String.concat " " scopes |> Uri.EscapeDataString
    let encodedRedirectUri = Uri.EscapeDataString redirectUri
    $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={clientId}&response_type=code&redirect_uri={encodedRedirectUri}&scope={encodedScope}"

let printResult refreshToken =
    let settings =
        {
            ClientId = clientId
            RefreshToken = refreshToken
        }

    let json = JsonSerializer.Serialize(settings, JsonSerializerOptions(WriteIndented = true))

    printfn "OneDrive settings JSON for settings/onedrive:"
    printfn "%s" json
    printfn ""
    printfn "setup-gcp.ps1 arguments:"
    printfn "-OneDriveClientId %s -OneDriveRefreshToken %s" clientId refreshToken

let writeCallbackResponse (context: HttpListenerContext) (statusCode: int) (html: string) =
    task {
        let bytes = Encoding.UTF8.GetBytes(html)
        context.Response.StatusCode <- statusCode
        context.Response.ContentType <- "text/html; charset=utf-8"
        context.Response.ContentLength64 <- int64 bytes.Length
        do! context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length)
        context.Response.OutputStream.Close()
    }

let openBrowser (url: string) =
    let startInfo = ProcessStartInfo(url)
    startInfo.UseShellExecute <- true
    Process.Start(startInfo) |> ignore

let waitForAuthorizationCode () =
    task {
        use listener = new HttpListener()
        listener.Prefixes.Add(listenerPrefix)
        listener.Start()

        let url = getAuthorizationUrl ()
        printfn "Listening for OAuth callback on %s" redirectUri
        printfn "Opening browser to:"
        printfn "%s" url
        printfn ""
        printfn "If the browser does not open, navigate to that URL manually."

        openBrowser url

        let! context = listener.GetContextAsync()
        let request = context.Request

        match request.QueryString["error"], request.QueryString["code"] with
        | error, _ when not (String.IsNullOrWhiteSpace(error)) ->
            let description = request.QueryString["error_description"]
            let message =
                if String.IsNullOrWhiteSpace(description) then error
                else $"{error}: {description}"

            do!
                writeCallbackResponse
                    context
                    400
                    "<html><body><h1>OneDrive authorization failed</h1><p>You can close this window and check the terminal output.</p></body></html>"

            return Error $"Authorization failed: {message}"
        | _, code when not (String.IsNullOrWhiteSpace(code)) ->
            do!
                writeCallbackResponse
                    context
                    200
                    "<html><body><h1>OneDrive authorization complete</h1><p>You can close this window and return to the terminal.</p></body></html>"

            return Ok code
        | _ ->
            do!
                writeCallbackResponse
                    context
                    400
                    "<html><body><h1>Missing authorization code</h1><p>No code query parameter was found in the callback.</p></body></html>"

            return Error "Callback did not contain an authorization code."
    }

let tryGetRequiredField (fieldName: string) (node: JsonNode) =
    match node[fieldName] with
    | null -> Error $"Token response did not contain '{fieldName}'."
    | value -> Ok (value.GetValue<string>())

let getAccessTokenFromCode code =
    task {
        let formValues =
            [
                KeyValuePair("client_id", clientId)
                KeyValuePair("code", code)
                KeyValuePair("redirect_uri", redirectUri)
                KeyValuePair("grant_type", "authorization_code")
            ]

        use body = new FormUrlEncodedContent(formValues)
        let! result = client.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/token", body)
        let! json = result.Content.ReadAsStringAsync()

        if not result.IsSuccessStatusCode then
            return Error $"Token request failed ({int result.StatusCode} {result.StatusCode}): {json}"
        else
            let node = JsonNode.Parse(json)

            match tryGetRequiredField "access_token" node, tryGetRequiredField "refresh_token" node with
            | Ok accessToken, Ok refreshToken -> return Ok (accessToken, refreshToken)
            | Error message, _ -> return Error $"{message} Response: {json}"
            | _, Error message -> return Error $"{message} Response: {json}"
    }

let printUsage () =
    printfn "Starts a localhost callback listener on %s, opens the Microsoft sign-in page, and prints the resulting settings JSON." redirectUri
    printfn ""
    printfn "Usage:"
    printfn "dotnet fsi .\\OneDriveAccessToken.fsx"

match fsi.CommandLineArgs |> Array.skip 1 with
| [||] ->
    match waitForAuthorizationCode () |> Async.AwaitTask |> Async.RunSynchronously with
    | Error message ->
        eprintfn "%s" message
        Environment.ExitCode <- 1
    | Ok code ->
        match getAccessTokenFromCode code |> Async.AwaitTask |> Async.RunSynchronously with
        | Ok (_, refreshToken) ->
            printResult refreshToken
        | Error message ->
            eprintfn "%s" message
            Environment.ExitCode <- 1
| [| "help" |]
| [| "--help" |]
| [| "-h" |] ->
    printUsage ()
| _ ->
    eprintfn "This script no longer accepts manual authorization code input."
    printUsage ()
    Environment.ExitCode <- 1