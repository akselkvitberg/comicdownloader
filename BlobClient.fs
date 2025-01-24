module BlobClient

open System.Text.RegularExpressions
open Azure
open Azure.Storage.Blobs
open System.Text.Json
open System

let private toOption (response: Response<'a>) = 
    if response.HasValue then Some response.Value
    else None

let private normalize (name:string) =
    Regex.Replace(name.ToLower(), "[^a-zA-Z0-9-]+", "")

let store container path (data:'a) (client: BlobServiceClient) =
    let container = normalize container
    let path = normalize path
    let containerClient = client.GetBlobContainerClient(container)
    containerClient.CreateIfNotExists() |> ignore
    let blobClient = containerClient.GetBlobClient(path)
    blobClient.DeleteIfExists() |> ignore
    let json = data |> JsonSerializer.Serialize |> BinaryData
    blobClient.Upload(json) |> toOption |> Option.isSome

let storeBinary container path (data: byte array) (client: BlobServiceClient) =
    let container = normalize container
    let path = normalize path
    let containerClient = client.GetBlobContainerClient(container)
    containerClient.CreateIfNotExists() |> ignore
    let blobClient = containerClient.GetBlobClient(path)
    blobClient.DeleteIfExists() |> ignore
    let binaryData = data |> BinaryData
    blobClient.Upload(binaryData) |> toOption |> Option.isSome

let load<'a> container path (client: BlobServiceClient) =
    let container = normalize container
    let path = normalize path
    let containerClient = client.GetBlobContainerClient(container)
    containerClient.CreateIfNotExists() |> ignore
    let blobClient = containerClient.GetBlobClient(path)
    match blobClient.Exists() |> toOption with
    | Some true -> 
        match blobClient.DownloadContent() |> toOption with
        | Some data -> 
            data.Content.ToString()
            |> JsonSerializer.Deserialize<'a>
            |> function | null -> None | v -> Some v
        | _ -> None
    | _ -> None
    
let exists container path (client: BlobServiceClient) =
    let container = normalize container
    let path = normalize path
    let containerClient = client.GetBlobContainerClient(container)
    containerClient.CreateIfNotExists() |> ignore
    let blobClient = containerClient.GetBlobClient(path)
    match blobClient.Exists() |> toOption with
    | Some true -> true
    | _ -> false