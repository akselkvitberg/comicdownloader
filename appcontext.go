package main

import (
	"context"
	"fmt"
	"os"
	"regexp"
	"strings"

	secretmanager "cloud.google.com/go/secretmanager/apiv1"
	"cloud.google.com/go/storage"
)

var normalizeRe = regexp.MustCompile(`[^a-zA-Z0-9-]+`)

func normalize(name string) string {
	return normalizeRe.ReplaceAllString(strings.ToLower(name), "")
}

func objectName(container, path string) string {
	return normalize(container) + "/" + normalize(path)
}

func secretID(prefix, container, path string) string {
	return prefix + normalize(container) + "-" + normalize(path)
}

type AppContext struct {
	StorageClient       *storage.Client
	SecretManagerClient *secretmanager.Client
	ProjectID           string
	BucketName          string
	SecretPrefix        string
	Ctx                 context.Context
}

func NewAppContext() (*AppContext, error) {
	ctx := context.Background()

	projectID := os.Getenv("GCP_PROJECT_ID")
	if projectID == "" {
		projectID = os.Getenv("GOOGLE_CLOUD_PROJECT")
	}
	if projectID == "" {
		return nil, fmt.Errorf("GCP_PROJECT_ID or GOOGLE_CLOUD_PROJECT must be set")
	}

	bucketName := os.Getenv("GCS_BUCKET_NAME")
	if bucketName == "" {
		return nil, fmt.Errorf("GCS_BUCKET_NAME must be set")
	}

	secretPrefix := os.Getenv("COMICDOWNLOADER_SECRET_PREFIX")
	if secretPrefix == "" {
		secretPrefix = "comicdownloader-"
	}

	storageClient, err := storage.NewClient(ctx)
	if err != nil {
		return nil, fmt.Errorf("storage.NewClient: %w", err)
	}

	smClient, err := secretmanager.NewClient(ctx)
	if err != nil {
		return nil, fmt.Errorf("secretmanager.NewClient: %w", err)
	}

	return &AppContext{
		StorageClient:       storageClient,
		SecretManagerClient: smClient,
		ProjectID:           projectID,
		BucketName:          bucketName,
		SecretPrefix:        secretPrefix,
		Ctx:                 ctx,
	}, nil
}
