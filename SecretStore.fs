module SecretStore

open Google.Cloud.SecretManager.V1
open Google.Protobuf

let load container path (context: StorageContext.Context) =
    try
        let versionName = SecretVersionName(context.ProjectId, StorageContext.secretId container path context, "latest")
        let response = context.SecretManagerClient.AccessSecretVersion(versionName)
        response.Payload.Data.ToStringUtf8() |> Some
    with ex when StorageContext.isNotFound ex ->
        None

// Versions kept after each store: the new one plus the previous as a fallback.
let private versionsToKeep = 2

// Every live (enabled or disabled) version is billed monthly, and the OneDrive
// token is re-stored daily, so destroy all but the newest few. Versions already
// scheduled for destruction (the secret's version_destroy_ttl) are skipped.
let private destroyOldVersions (secretName: SecretName) (context: StorageContext.Context) =
    let request =
        ListSecretVersionsRequest(ParentAsSecretName = secretName, Filter = "state:ENABLED OR state:DISABLED")

    context.SecretManagerClient.ListSecretVersions(request)
    |> Seq.filter (fun version -> isNull version.ScheduledDestroyTime)
    |> Seq.sortByDescending (fun version -> version.CreateTime.ToDateTime())
    |> Seq.skip versionsToKeep
    |> Seq.toList
    |> List.iter (fun version -> context.SecretManagerClient.DestroySecretVersion(version.SecretVersionName) |> ignore)

let store container path (data: string) (context: StorageContext.Context) =
    let secretName = SecretName(context.ProjectId, StorageContext.secretId container path context)
    let payload = SecretPayload(Data = ByteString.CopyFromUtf8(data))
    context.SecretManagerClient.AddSecretVersion(secretName, payload) |> ignore
    destroyOldVersions secretName context
    true