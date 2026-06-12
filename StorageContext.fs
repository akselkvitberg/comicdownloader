module StorageContext

open System
open System.Net
open System.Text.RegularExpressions
open Google
open Google.Cloud.SecretManager.V1
open Google.Cloud.Storage.V1
open Grpc.Core

type Context = {
    StorageClient: StorageClient
    SecretManagerClient: SecretManagerServiceClient
    ProjectId: string
    BucketName: string
    SecretPrefix: string
    CalvinAndHobbesBucketName: string option
}

let normalize (name: string) =
    Regex.Replace(name.ToLower(), "[^a-zA-Z0-9-]+", "")

let secretId container path (context: Context) =
    $"{context.SecretPrefix}{normalize container}-{normalize path}"

let objectName container path =
    $"{normalize container}/{normalize path}"

let isNotFound (ex: exn) =
    match ex with
    | :? GoogleApiException as googleEx when googleEx.HttpStatusCode = HttpStatusCode.NotFound -> true
    | :? RpcException as rpcEx when rpcEx.StatusCode = StatusCode.NotFound -> true
    | _ -> false