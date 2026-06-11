package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
)

const (
	tokenURL    = "https://login.microsoftonline.com/common/oauth2/v2.0/token"
	graphAPIURL = "https://graph.microsoft.com/v1.0"
	chunkSize   = 320 * 1024
)

type tokenResponse struct {
	AccessToken  string `json:"access_token"`
	RefreshToken string `json:"refresh_token"`
}

type uploadSession struct {
	UploadURL string `json:"uploadUrl"`
}

func RefreshToken(clientID, refreshToken string) (accessToken, newRefreshToken string, err error) {
	form := url.Values{}
	form.Set("client_id", clientID)
	form.Set("grant_type", "refresh_token")
	form.Set("refresh_token", refreshToken)
	form.Set("redirect_uri", "http://localhost:8000")
	form.Set("scope", "Files.ReadWrite offline_access")

	resp, err := http.PostForm(tokenURL, form)
	if err != nil {
		return "", "", fmt.Errorf("token request: %w", err)
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(resp.Body)
	if resp.StatusCode != 200 {
		return "", "", fmt.Errorf("token request failed %d: %s", resp.StatusCode, body)
	}
	var tr tokenResponse
	if err := json.Unmarshal(body, &tr); err != nil {
		return "", "", fmt.Errorf("unmarshal token: %w", err)
	}
	return tr.AccessToken, tr.RefreshToken, nil
}

func UploadToOneDrive(accessToken, folderName, fileName string, data []byte) error {
	if len(data) < 4*1024*1024 {
		return simpleUpload(accessToken, folderName, fileName, data)
	}
	return chunkedUpload(accessToken, folderName, fileName, data)
}

func simpleUpload(accessToken, folderName, fileName string, data []byte) error {
	u := fmt.Sprintf("%s/me/drive/special/approot:/%s/%s:/content", graphAPIURL, folderName, fileName)
	req, err := http.NewRequest("PUT", u, bytes.NewReader(data))
	if err != nil {
		return fmt.Errorf("simple upload request: %w", err)
	}
	req.Header.Set("Authorization", "Bearer "+accessToken)
	req.Header.Set("Content-Type", "application/octet-stream")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return fmt.Errorf("simple upload: %w", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode >= 300 {
		body, _ := io.ReadAll(resp.Body)
		return fmt.Errorf("simple upload %d: %s", resp.StatusCode, body)
	}
	return nil
}

func chunkedUpload(accessToken, folderName, fileName string, data []byte) error {
	sessionURL := fmt.Sprintf("%s/me/drive/special/approot:/%s/%s:/createUploadSession", graphAPIURL, folderName, fileName)
	req, err := http.NewRequest("POST", sessionURL, strings.NewReader(`{"item":{"@microsoft.graph.conflictBehavior":"replace"}}`))
	if err != nil {
		return fmt.Errorf("create session request: %w", err)
	}
	req.Header.Set("Authorization", "Bearer "+accessToken)
	req.Header.Set("Content-Type", "application/json")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return fmt.Errorf("create upload session: %w", err)
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(resp.Body)
	var session uploadSession
	if err := json.Unmarshal(body, &session); err != nil {
		return fmt.Errorf("unmarshal session: %w", err)
	}

	total := len(data)
	for offset := 0; offset < total; offset += chunkSize {
		end := offset + chunkSize
		if end > total {
			end = total
		}
		chunk := data[offset:end]
		chunkReq, err := http.NewRequest("PUT", session.UploadURL, bytes.NewReader(chunk))
		if err != nil {
			return fmt.Errorf("chunk request at %d: %w", offset, err)
		}
		chunkReq.Header.Set("Content-Range", fmt.Sprintf("bytes %d-%d/%d", offset, end-1, total))
		chunkReq.Header.Set("Content-Type", "application/octet-stream")
		chunkResp, err := http.DefaultClient.Do(chunkReq)
		if err != nil {
			return fmt.Errorf("upload chunk at %d: %w", offset, err)
		}
		io.Copy(io.Discard, chunkResp.Body)
		chunkResp.Body.Close()
		if chunkResp.StatusCode >= 400 {
			return fmt.Errorf("upload chunk %d: status %d", offset, chunkResp.StatusCode)
		}
	}
	return nil
}
