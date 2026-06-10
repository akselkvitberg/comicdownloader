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

let store container path (data: string) (context: StorageContext.Context) =
    let secretName = SecretName(context.ProjectId, StorageContext.secretId container path context)
    let payload = SecretPayload(Data = ByteString.CopyFromUtf8(data))
    context.SecretManagerClient.AddSecretVersion(secretName, payload) |> ignore
    true