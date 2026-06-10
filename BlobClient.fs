module BlobClient

open System
open System.Text
open System.Text.Json

let private serialize<'a> (data: 'a) =
    match box data with
    | :? string as value -> value
    | _ -> JsonSerializer.Serialize data

let private deserialize<'a> (data: string) =
    if typeof<'a> = typeof<string> then
        Some (data |> box |> unbox<'a>)
    else
        data
        |> JsonSerializer.Deserialize<'a>
        |> function
            | null -> None
            | value -> Some value

let private imageContentType (data: byte array) =
    let header = data |> Array.truncate 4

    match header with
    | [| 0xFFuy; 0xD8uy; _; _ |] -> "image/jpeg"
    | [| 0x42uy; 0x4Duy; _; _ |] -> "image/bmp"
    | [| 0x47uy; 0x49uy; 0x46uy; _ |] -> "image/gif"
    | [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy |] -> "image/png"
    | _ -> "application/octet-stream"

let store container path (data:'a) (context: StorageContext.Context) =
    let serialized = serialize data

    if StorageContext.normalize container = "settings" then
        SecretStore.store container path serialized context
    else
        serialized
        |> Encoding.UTF8.GetBytes
        |> fun bytes -> ObjectStore.store container path "application/json" bytes context

let storeBinary container path (data: byte array) (context: StorageContext.Context) =
    ObjectStore.store container path (imageContentType data) data context

let load<'a> container path (context: StorageContext.Context) =
    let data =
        if StorageContext.normalize container = "settings" then
            SecretStore.load container path context
        else
            ObjectStore.load container path context

    data |> Option.bind deserialize<'a>
    
let exists container path (context: StorageContext.Context) =
    ObjectStore.exists container path context