package utils

import (
	"archive/zip"
	"encoding/json"
	"fmt"
	"io"
)

// ExtractZipJSON 从 ZIP 文件中提取指定名称的 JSON 文件内容
func ExtractZipJSON(zipPath, entryName string) (map[string]interface{}, error) {
	reader, err := zip.OpenReader(zipPath)
	if err != nil {
		return nil, fmt.Errorf("failed to open zip: %w", err)
	}
	defer reader.Close()

	for _, file := range reader.File {
		if file.Name == entryName {
			rc, err := file.Open()
			if err != nil {
				return nil, fmt.Errorf("failed to open entry: %w", err)
			}
			defer rc.Close()

			data, err := io.ReadAll(rc)
			if err != nil {
				return nil, fmt.Errorf("failed to read entry: %w", err)
			}

			var result map[string]interface{}
			if err := json.Unmarshal(data, &result); err != nil {
				return nil, fmt.Errorf("failed to unmarshal JSON: %w", err)
			}
			return result, nil
		}
	}

	return nil, fmt.Errorf("entry %s not found in zip", entryName)
}

// ExtractZipEntry 从 ZIP 文件中提取指定名称的文件内容为字节数组
func ExtractZipEntry(zipPath, entryName string) ([]byte, error) {
	reader, err := zip.OpenReader(zipPath)
	if err != nil {
		return nil, fmt.Errorf("failed to open zip: %w", err)
	}
	defer reader.Close()

	for _, file := range reader.File {
		if file.Name == entryName {
			rc, err := file.Open()
			if err != nil {
				return nil, fmt.Errorf("failed to open entry: %w", err)
			}
			defer rc.Close()

			return io.ReadAll(rc)
		}
	}

	return nil, fmt.Errorf("entry %s not found in zip", entryName)
}
