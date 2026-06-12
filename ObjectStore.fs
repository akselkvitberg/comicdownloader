module ObjectStore

open System.IO
open System.Text

let load container path (context: StorageContext.Context) =
    try
        use stream = new MemoryStream()
        context.StorageClient.DownloadObject(context.BucketName, StorageContext.objectName container path, stream)
        |> ignore
        stream.ToArray() |> Encoding.UTF8.GetString |> Some
    with ex when StorageContext.isNotFound ex ->
        None

let loadBinary bucketName objectName (context: StorageContext.Context) =
    try
        use stream = new MemoryStream()
        context.StorageClient.DownloadObject(bucketName, objectName, stream)
        |> ignore
        stream.ToArray() |> Some
    with ex when StorageContext.isNotFound ex ->
        None

let exists container path (context: StorageContext.Context) =
    try
        context.StorageClient.GetObject(context.BucketName, StorageContext.objectName container path) |> ignore
        true
    with ex when StorageContext.isNotFound ex ->
        false

let store container path contentType (data: byte array) (context: StorageContext.Context) =
    use stream = new MemoryStream(data)
    context.StorageClient.UploadObject(context.BucketName, StorageContext.objectName container path, contentType, stream)
    |> ignore
    true