package main

import (
	"fmt"
	"io"
	"strings"

	"cloud.google.com/go/storage"
)

func (app *AppContext) BlobExists(container, path string) (bool, error) {
	key := objectName(container, path)
	_, err := app.StorageClient.Bucket(app.BucketName).Object(key).Attrs(app.Ctx)
	if err == storage.ErrObjectNotExist {
		return false, nil
	}
	if err != nil {
		return false, fmt.Errorf("BlobExists %s: %w", key, err)
	}
	return true, nil
}

func (app *AppContext) WriteBlob(container, path, contentType string, data []byte) error {
	key := objectName(container, path)
	w := app.StorageClient.Bucket(app.BucketName).Object(key).NewWriter(app.Ctx)
	w.ContentType = contentType
	if _, err := w.Write(data); err != nil {
		w.Close()
		return fmt.Errorf("WriteBlob write %s: %w", key, err)
	}
	if err := w.Close(); err != nil {
		return fmt.Errorf("WriteBlob close %s: %w", key, err)
	}
	return nil
}

func (app *AppContext) ReadBlobString(container, path string) (string, bool, error) {
	key := objectName(container, path)
	r, err := app.StorageClient.Bucket(app.BucketName).Object(key).NewReader(app.Ctx)
	if err == storage.ErrObjectNotExist {
		return "", false, nil
	}
	if err != nil {
		return "", false, fmt.Errorf("ReadBlobString %s: %w", key, err)
	}
	defer r.Close()
	b, err := io.ReadAll(r)
	if err != nil {
		return "", false, fmt.Errorf("ReadBlobString read %s: %w", key, err)
	}
	return strings.TrimSpace(string(b)), true, nil
}
