package main

import (
	"encoding/json"
	"fmt"

	secretmanagerpb "cloud.google.com/go/secretmanager/apiv1/secretmanagerpb"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/status"
)

type OneDriveSettings struct {
	ClientID     string `json:"ClientId"`
	RefreshToken string `json:"RefreshToken"`
}

type TelegramSettings struct {
	ApiKey string `json:"ApiKey"`
	User   string `json:"User"`
}

func isNotFoundError(err error) bool {
	s, ok := status.FromError(err)
	return ok && s.Code() == codes.NotFound
}

func (app *AppContext) GetSecret(container, path string) (string, bool, error) {
	name := fmt.Sprintf("projects/%s/secrets/%s/versions/latest",
		app.ProjectID, secretID(app.SecretPrefix, container, path))
	result, err := app.SecretManagerClient.AccessSecretVersion(app.Ctx,
		&secretmanagerpb.AccessSecretVersionRequest{Name: name})
	if err != nil {
		if isNotFoundError(err) {
			return "", false, nil
		}
		return "", false, fmt.Errorf("AccessSecretVersion %s/%s: %w", container, path, err)
	}
	return string(result.Payload.Data), true, nil
}

func (app *AppContext) PutSecret(container, path, data string) error {
	parent := fmt.Sprintf("projects/%s/secrets/%s",
		app.ProjectID, secretID(app.SecretPrefix, container, path))
	_, err := app.SecretManagerClient.AddSecretVersion(app.Ctx,
		&secretmanagerpb.AddSecretVersionRequest{
			Parent:  parent,
			Payload: &secretmanagerpb.SecretPayload{Data: []byte(data)},
		})
	if err != nil {
		return fmt.Errorf("AddSecretVersion %s/%s: %w", container, path, err)
	}
	return nil
}

func (app *AppContext) GetOneDriveSettings() (*OneDriveSettings, error) {
	data, ok, err := app.GetSecret("settings", "onedrive")
	if err != nil || !ok {
		return nil, err
	}
	var s OneDriveSettings
	if err := json.Unmarshal([]byte(data), &s); err != nil {
		return nil, fmt.Errorf("unmarshal OneDrive settings: %w", err)
	}
	return &s, nil
}

func (app *AppContext) GetTelegramSettings() (*TelegramSettings, error) {
	data, ok, err := app.GetSecret("settings", "telegram")
	if err != nil || !ok {
		return nil, err
	}
	var s TelegramSettings
	if err := json.Unmarshal([]byte(data), &s); err != nil {
		return nil, fmt.Errorf("unmarshal Telegram settings: %w", err)
	}
	return &s, nil
}

func (app *AppContext) PutOneDriveSettings(s *OneDriveSettings) error {
	data, err := json.Marshal(s)
	if err != nil {
		return err
	}
	return app.PutSecret("settings", "onedrive", string(data))
}
