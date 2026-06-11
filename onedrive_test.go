package main

import (
	"encoding/json"
	"testing"
)

func TestTokenResponseParsing(t *testing.T) {
	var tr tokenResponse
	if err := json.Unmarshal([]byte(`{"access_token":"acc","refresh_token":"ref"}`), &tr); err != nil {
		t.Fatal(err)
	}
	if tr.AccessToken != "acc" {
		t.Errorf("AccessToken = %q", tr.AccessToken)
	}
	if tr.RefreshToken != "ref" {
		t.Errorf("RefreshToken = %q", tr.RefreshToken)
	}
}
