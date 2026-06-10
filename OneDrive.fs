module comicdownloader.OneDrive


open System.Threading.Tasks
open Microsoft.Graph
open System.Net.Http
open System.Collections.Generic
open System.Text
open System.Text.Json.Nodes
open Azure.Core
open System
open System.IO
open Microsoft.Graph.Models
open Microsoft.Extensions.Logging

let client = new HttpClient();

type OneDriveSettings = {
    ClientId: string
    RefreshToken: string
}

let getAccessToken (logger: ILogger) settings =
    task {
        logger.LogInformation("Refreshing OneDrive access token")

        let formValues =
            [
                KeyValuePair<string, string>("client_id", settings.ClientId)
                KeyValuePair<string, string>("refresh_token", settings.RefreshToken)
                KeyValuePair<string, string>("redirect_uri", "http://localhost:8000")
                KeyValuePair<string, string>("grant_type", "refresh_token")
            ]

        use body = new FormUrlEncodedContent(formValues)
        let! result = client.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/token", body)
        let! json = result.Content.ReadAsStringAsync()

        if not result.IsSuccessStatusCode then
            logger.LogError("OneDrive token refresh failed with status {StatusCode}", int result.StatusCode)
            return raise (InvalidOperationException($"OneDrive token refresh failed ({int result.StatusCode} {result.StatusCode}): {json}"))
        else
            let node = JsonNode.Parse(json);
            let refreshToken = node["refresh_token"].GetValue<string>()
            let accessToken = node["access_token"].GetValue<string>();

            logger.LogInformation("OneDrive access token refreshed successfully")

            return accessToken, refreshToken
    }

let uploadFile (logger: ILogger) (settings) folderName fileName (bytes:byte array) =
    match settings with
    | None ->
        logger.LogWarning("Skipping OneDrive upload for {FolderName}/{FileName} because no OneDrive settings were found", folderName, fileName)
        Task.FromResult(Error (exn("No settings")))
    | Some settings ->
        task {
            logger.LogInformation("Uploading {FileName} to OneDrive folder {FolderName}", fileName, folderName)

            let! accessToken, _ = getAccessToken logger settings

            let token = DelegatedTokenCredential.Create(fun _ _ -> AccessToken(accessToken, DateTimeOffset.Now.AddMinutes(1)))
            let client = new GraphServiceClient(token)

            try
                use ms = new MemoryStream(bytes)
                let totalLength = bytes.Length
                let! drive = client.Me.Drive.GetAsync()
                let driveId = drive.Id

                let! appRoot = client.Drives[driveId].Special["AppRoot"].GetAsync()
                
                if totalLength > 4 * 1024 * 1024 then
                    let request = client.Drives[driveId].Items[appRoot.Id].ItemWithPath($"{folderName}/{fileName}").CreateUploadSession

                    let uploadSessionRequestBody = 
                        Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody(
                            Item = DriveItemUploadableProperties(AdditionalData = dict [ "@microsoft.graph.conflictBehavior", box "replace"])
                        )

                    let! uploadSession = request.PostAsync(uploadSessionRequestBody)
                    
                    let maxSliceSize = 320 * 1024
                    let fileUploadTask = LargeFileUploadTask<DriveItem>(uploadSession, ms, maxSliceSize)

                    let! _ = fileUploadTask.UploadAsync()
                    logger.LogInformation("OneDrive upload completed for {FolderName}/{FileName}", folderName, fileName)
                    return (Ok "File uploaded")
                else
                    let! _ = client.Drives[driveId].Items[appRoot.Id].ItemWithPath($"{folderName}/{fileName}").Content.PutAsync(ms)
                    logger.LogInformation("OneDrive upload completed for {FolderName}/{FileName}", folderName, fileName)
                    return (Ok "File uploaded")
            with
            | e -> 
                logger.LogError(e, "OneDrive upload failed for {FolderName}/{FileName}", folderName, fileName)
                return (Error e)
        }