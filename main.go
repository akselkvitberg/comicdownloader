package main

import (
	"encoding/json"
	"fmt"
	"log/slog"
	"net/http"
	"os"
	"time"
)

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	json.NewEncoder(w).Encode(v)
}

func main() {
	logger := slog.New(slog.NewJSONHandler(os.Stdout, nil))

	app, err := NewAppContext()
	if err != nil {
		logger.Error("Failed to initialize", "error", err)
		os.Exit(1)
	}

	mux := http.NewServeMux()

	mux.HandleFunc("GET /health", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, 200, map[string]string{"status": "ok", "service": "comicdownloader"})
	})

	mux.HandleFunc("POST /jobs/download", func(w http.ResponseWriter, r *http.Request) {
		jobLogger := logger.With("job", "download")
		jobLogger.Info("Job started", "executedAt", time.Now().UTC())
		if err := RunDownload(app, jobLogger); err != nil {
			jobLogger.Error("Job failed", "error", err)
			writeJSON(w, 500, map[string]any{"job": "download", "status": "error", "message": err.Error()})
			return
		}
		writeJSON(w, 200, map[string]any{"job": "download", "status": "ok", "executedAt": time.Now().UTC()})
	})

	mux.HandleFunc("POST /jobs/refresh-onedrive", func(w http.ResponseWriter, r *http.Request) {
		jobLogger := logger.With("job", "refresh-onedrive")
		jobLogger.Info("Job started", "executedAt", time.Now().UTC())
		if err := RunRefreshOneDriveToken(app, jobLogger); err != nil {
			jobLogger.Error("Job failed", "error", err)
			writeJSON(w, 500, map[string]any{"job": "refresh-onedrive", "status": "error", "message": err.Error()})
			return
		}
		writeJSON(w, 200, map[string]any{"job": "refresh-onedrive", "status": "ok", "executedAt": time.Now().UTC()})
	})

	port := os.Getenv("PORT")
	if port == "" {
		port = "8080"
	}
	addr := fmt.Sprintf(":%s", port)
	logger.Info("Starting server", "addr", addr)
	if err := http.ListenAndServe(addr, mux); err != nil {
		logger.Error("Server failed", "error", err)
		os.Exit(1)
	}
}
