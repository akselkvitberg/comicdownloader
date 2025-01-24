module comicdownloader.Telegram

open System.Threading.Tasks
open Telegram.Bot
open Telegram.Bot.Types
open System.Net.Http
open System.IO

let client = new HttpClient()

type TelegramSettings = {
    ApiKey: string
    User: string
}

let sendMessage settings (image: byte array) =
    match settings with
    | None -> Task.FromResult(Error (exn("No settings")))
    | Some settings ->
        let telegramClient = TelegramBotClient(settings.ApiKey, client)
        use ms = new MemoryStream(image)
        task {
            try
                let! _ = telegramClient.SendPhoto(settings.User, InputFile.FromStream(ms))
                return Ok "Sent"
            with
                | exn -> return Error exn
        }
