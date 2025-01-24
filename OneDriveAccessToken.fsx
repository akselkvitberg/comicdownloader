open System.Net.Http
open System.Text
open System.Text.Json.Nodes

let client = new HttpClient();
let clientId = "87424810-3904-4ad6-8e6a-07ec846f7353"
let scope =  "files.readwrite.appfolder%20offline_access"

let getInitialAccessToken = 
    let scope =  "files.readwrite.appfolder%20offline_access"
    let redirectUri = "http://localhost:8000"
    let url = $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={clientId}&scope={scope}&response_type=code&redirect_uri={redirectUri}"
    printfn "%s" url
    //System.Diagnostics.Process.Start(url)

let getAccessTokenFromCode code =
    let body = $"client_id={clientId}&code={code}&redirect_uri=http://localhost:8000&grant_type=authorization_code";
    async {
        let! result = client.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/token", new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")) |> Async.AwaitTask
        let! json = result.Content.ReadAsStringAsync() |> Async.AwaitTask;
        printfn "%s" json
        
        let node = JsonNode.Parse(json);
        let refreshToken = node["refresh_token"].GetValue<string>()
        let accessToken = node["access_token"].GetValue<string>();

        return accessToken, refreshToken
    }
    
getInitialAccessToken

let accessToken, refreshToken = getAccessTokenFromCode "M.C529_SN1.2.U.a14db3a3-7c41-4022-6f6a-aa5eb33d7a2d" |> Async.RunSynchronously

printfn $"%s{refreshToken}"