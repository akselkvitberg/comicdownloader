module comicdownloader.OneDrive


open System.Threading.Tasks
open Microsoft.Graph
open System.Net.Http
open System.Text
open System.Text.Json.Nodes
open Azure.Core
open System
open System.IO
open Microsoft.Graph.Models

let client = new HttpClient();

type OneDriveSettings = {
    ClientId: string
    RefreshToken: string
}

let getAccessToken settings =
    let body = $"client_id={settings.ClientId}&refresh_token={settings.RefreshToken}&redirect_uri=http://localhost:8000&grant_type=refresh_token";
    task {
        let! result = client.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/token", new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"))
        let! json = result.Content.ReadAsStringAsync()
        
        let node = JsonNode.Parse(json);
        let refreshToken = node["refresh_token"].GetValue<string>()
        let accessToken = node["access_token"].GetValue<string>();

        return accessToken, refreshToken
    }

let uploadFile (settings) folderName fileName (bytes:byte array) =
    match settings with
    | None -> Task.FromResult(Error (exn("No settings")))
    | Some settings ->
        task {
            let! accessToken, _ = getAccessToken settings

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
                    return (Ok "File uploaded")
                else
                    let! _ = client.Drives[driveId].Items[appRoot.Id].ItemWithPath($"{folderName}/{fileName}").Content.PutAsync(ms)
                    return (Ok "File uploaded")
            with
            | e -> 
                return (Error e)
        }