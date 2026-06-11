package main

import (
	"encoding/json"
	"testing"
)

func TestOneDriveSettingsUnmarshal(t *testing.T) {
	var s OneDriveSettings
	if err := json.Unmarshal([]byte(`{"ClientId":"cid","RefreshToken":"rtok"}`), &s); err != nil {
		t.Fatal(err)
	}
	if s.ClientID != "cid" {
		t.Errorf("ClientID = %q", s.ClientID)
	}
	if s.RefreshToken != "rtok" {
		t.Errorf("RefreshToken = %q", s.RefreshToken)
	}
}

func TestTelegramSettingsUnmarshal(t *testing.T) {
	var s TelegramSettings
	if err := json.Unmarshal([]byte(`{"ApiKey":"key","User":"12345678"}`), &s); err != nil {
		t.Fatal(err)
	}
	if s.ApiKey != "key" {
		t.Errorf("ApiKey = %q", s.ApiKey)
	}
	if s.User != "12345678" {
		t.Errorf("User = %q", s.User)
	}
}
