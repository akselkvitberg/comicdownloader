module comicdownloader.Telegram

open System
open System.Threading.Tasks
open Telegram.Bot
open Telegram.Bot.Types
open System.Net.Http
open System.IO
open Microsoft.Extensions.Logging

let client = new HttpClient()

type TelegramSettings = {
    ApiKey: string
    User: string
}

let sendText (logger: ILogger) settings (message: string) =
    match settings with
    | None ->
        logger.LogWarning("Skipping Telegram text message because no Telegram settings were found")
        Task.FromResult(Error (exn("No settings")))
    | Some settings ->
        let telegramClient = TelegramBotClient(settings.ApiKey, client)
        task {
            try
                logger.LogInformation("Sending Telegram text message")
                let! _ = telegramClient.SendMessage(settings.User, message)
                logger.LogInformation("Telegram text message sent")
                return Ok "Sent"
            with
                | exn ->
                    logger.LogError(exn, "Telegram text message failed")
                    return Error exn
        }

let private normalizeCaption caption =
    caption
    |> Option.bind (fun text ->
        if String.IsNullOrWhiteSpace(text) then
            None
        elif text.Length > 1024 then
            Some(text.Substring(0, 1024))
        else
            Some text)

let sendMessage (logger: ILogger) settings (image: byte array) caption =
    match settings with
    | None ->
        logger.LogWarning("Skipping Telegram image send because no Telegram settings were found")
        Task.FromResult(Error (exn("No settings")))
    | Some settings ->
        let telegramClient = TelegramBotClient(settings.ApiKey, client)
        task {
            try
                use ms = new MemoryStream(image)
                let caption = normalizeCaption caption

                logger.LogInformation(
                    "Sending Telegram image with {ByteCount} bytes. Caption included: {HasCaption}",
                    image.Length,
                    Option.isSome caption
                )

                let! _ =
                    match caption with
                    | Some caption -> telegramClient.SendPhoto(settings.User, InputFile.FromStream(ms), caption = caption)
                    | None -> telegramClient.SendPhoto(settings.User, InputFile.FromStream(ms))

                logger.LogInformation("Telegram image sent")
                return Ok "Sent"
            with
                | exn ->
                    logger.LogError(exn, "Telegram image send failed")
                    return Error exn
        }
