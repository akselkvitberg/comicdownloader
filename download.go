package main

import (
	"fmt"
	"log/slog"
)

type comicJob struct {
	name     string
	download func() (*ComicResult, error)
}

func RunDownload(app *AppContext, logger *slog.Logger) error {
	logger.Info("Loading settings")

	oneDriveSettings, err := app.GetOneDriveSettings()
	if err != nil {
		logger.Warn("Could not load OneDrive settings", "error", err)
	}
	telegramSettings, err := app.GetTelegramSettings()
	if err != nil {
		logger.Warn("Could not load Telegram settings", "error", err)
	}

	logger.Info("Settings loaded",
		"hasOneDrive", oneDriveSettings != nil,
		"hasTelegram", telegramSettings != nil)

	var tgClient *TelegramClient
	if telegramSettings != nil {
		tgClient, err = NewTelegramClient(telegramSettings)
		if err != nil {
			logger.Warn("Could not create Telegram client", "error", err)
		}
	}

	comics := []comicJob{
		{"Lunch TU", DownloadLunchTU},
		{"XKCD", DownloadXKCD},
		{"Lunch E24", DownloadLunchE24},
		{"The Far Side", DownloadFarSide},
		{"Swords", func() (*ComicResult, error) {
			return DownloadRSS("https://swordscomic.com/comic/feed/")
		}},
	}

	logger.Info("Starting comic download batch")
	for _, comic := range comics {
		processComic(app, logger, oneDriveSettings, tgClient, comic)
	}
	logger.Info("Comic download batch completed")
	return nil
}

func processComic(app *AppContext, logger *slog.Logger, oneDrive *OneDriveSettings, tg *TelegramClient, job comicJob) {
	logger.Info("Starting comic download", "comic", job.name)

	result, err := job.download()
	if err != nil {
		logger.Error("Could not download comic", "comic", job.name, "error", err)
		sendErrorToTelegram(logger, tg, job.name, err)
		return
	}
	logger.Info("Downloaded comic", "comic", job.name, "bytes", len(result.Data))

	hash, err := hashImagePixels(result.Data)
	if err != nil {
		logger.Error("Could not hash comic", "comic", job.name, "error", err)
		sendErrorToTelegram(logger, tg, job.name, err)
		return
	}

	exists, err := app.BlobExists(job.name, hash)
	if err != nil {
		logger.Error("BlobExists check failed", "comic", job.name, "error", err)
		sendErrorToTelegram(logger, tg, job.name, err)
		return
	}
	if exists {
		logger.Info("Skipping duplicate comic", "comic", job.name, "hash", hash)
		return
	}

	logger.Info("New comic detected", "comic", job.name, "hash", hash)
	fileName := getFileName(result.Data)

	if oneDrive != nil {
		accessToken, _, odErr := RefreshToken(oneDrive.ClientID, oneDrive.RefreshToken)
		if odErr != nil {
			logger.Warn("OneDrive token refresh failed", "comic", job.name, "error", odErr)
		} else if odErr = UploadToOneDrive(accessToken, job.name, fileName, result.Data); odErr != nil {
			logger.Warn("OneDrive upload failed", "comic", job.name, "error", odErr)
		} else {
			logger.Info("OneDrive upload succeeded", "comic", job.name)
		}
	} else {
		logger.Warn("Skipping OneDrive (no settings)", "comic", job.name)
	}

	if tg != nil {
		caption := normalizeCaption(captionStr(result.Caption))
		if tgErr := tg.SendPhoto(result.Data, fileName, caption); tgErr != nil {
			logger.Warn("Telegram send failed", "comic", job.name, "error", tgErr)
		} else {
			logger.Info("Telegram send succeeded", "comic", job.name)
		}
	} else {
		logger.Warn("Skipping Telegram (no settings)", "comic", job.name)
	}

	ct := detectContentType(result.Data)
	if err := app.WriteBlob(job.name, hash, ct, result.Data); err != nil {
		logger.Error("Could not store comic in GCS", "comic", job.name, "error", err)
		return
	}
	logger.Info("Stored comic in GCS", "comic", job.name, "hash", hash)
}

func captionStr(caption *string) string {
	if caption == nil {
		return ""
	}
	return *caption
}

func sendErrorToTelegram(logger *slog.Logger, tg *TelegramClient, comicName string, err error) {
	if tg == nil {
		return
	}
	msg := fmt.Sprintf("Comic download failed for %s: %s", comicName, err.Error())
	if len(msg) > 4000 {
		msg = msg[:4000]
	}
	if tgErr := tg.SendText(msg); tgErr != nil {
		logger.Warn("Could not send Telegram error message", "error", tgErr)
	}
}

func RunRefreshOneDriveToken(app *AppContext, logger *slog.Logger) error {
	logger.Info("Starting OneDrive token refresh")

	settings, err := app.GetOneDriveSettings()
	if err != nil {
		return fmt.Errorf("load OneDrive settings: %w", err)
	}
	if settings == nil {
		logger.Warn("Skipping OneDrive refresh (no settings)")
		return nil
	}

	logger.Info("Loaded OneDrive settings")
	_, newRefresh, err := RefreshToken(settings.ClientID, settings.RefreshToken)
	if err != nil {
		return fmt.Errorf("refresh token: %w", err)
	}

	settings.RefreshToken = newRefresh
	if err := app.PutOneDriveSettings(settings); err != nil {
		return fmt.Errorf("store refreshed token: %w", err)
	}
	logger.Info("Stored refreshed OneDrive token")
	return nil
}
