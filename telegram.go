package main

import (
	"fmt"
	"strconv"
	"strings"

	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api/v5"
)

type TelegramClient struct {
	bot    *tgbotapi.BotAPI
	chatID int64
}

func NewTelegramClient(settings *TelegramSettings) (*TelegramClient, error) {
	chatID, err := strconv.ParseInt(settings.User, 10, 64)
	if err != nil {
		return nil, fmt.Errorf("invalid Telegram User %q: %w", settings.User, err)
	}
	bot, err := tgbotapi.NewBotAPI(settings.ApiKey)
	if err != nil {
		return nil, fmt.Errorf("create telegram bot: %w", err)
	}
	return &TelegramClient{bot: bot, chatID: chatID}, nil
}

func normalizeCaption(text string) *string {
	if strings.TrimSpace(text) == "" {
		return nil
	}
	if len(text) > 1024 {
		text = text[:1024]
	}
	return &text
}

func (t *TelegramClient) SendPhoto(data []byte, filename string, caption *string) error {
	msg := tgbotapi.NewPhoto(t.chatID, tgbotapi.FileBytes{Name: filename, Bytes: data})
	if caption != nil {
		msg.Caption = *caption
	}
	_, err := t.bot.Send(msg)
	return err
}

func (t *TelegramClient) SendText(text string) error {
	if len(text) > 4000 {
		text = text[:4000]
	}
	_, err := t.bot.Send(tgbotapi.NewMessage(t.chatID, text))
	return err
}
