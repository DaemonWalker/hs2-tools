package scanner

import (
	"archive/zip"
	"encoding/xml"
	"fmt"
	"io"
	"strings"
	"sync"
)

// cleanString 清理字符串，只保留 ASCII 可见字符
func cleanString(str string) string {
	var result strings.Builder
	result.Grow(len(str))

	for _, r := range str {
		if r >= 32 && r <= 126 {
			result.WriteRune(r)
		}
	}

	return strings.TrimSpace(result.String())
}

// Manifest XML manifest 结构
type Manifest struct {
	XMLName xml.Name `xml:"manifest"`
	Guid    string   `xml:"guid"`
	Name    string   `xml:"name"`
	Version string   `xml:"version"`
}

// ReadZipMod 解析 zipmod 文件，提取 manifest.xml 中的信息
func ReadZipMod(filePath string) (ModModel, error) {
	reader, err := zip.OpenReader(filePath)
	if err != nil {
		return nil, fmt.Errorf("failed to open zipmod: %w", err)
	}
	defer reader.Close()

	var manifestFile *zip.File
	for _, file := range reader.File {
		if strings.EqualFold(file.Name, "manifest.xml") {
			manifestFile = file
			break
		}
	}

	if manifestFile == nil {
		return nil, fmt.Errorf("manifest.xml not found in zipmod")
	}

	rc, err := manifestFile.Open()
	if err != nil {
		return nil, fmt.Errorf("failed to open manifest.xml: %w", err)
	}
	defer rc.Close()

	content, err := io.ReadAll(rc)
	if err != nil {
		return nil, fmt.Errorf("failed to read manifest.xml: %w", err)
	}

	var manifest Manifest
	if err := xml.Unmarshal(content, &manifest); err != nil {
		return nil, fmt.Errorf("failed to parse manifest.xml: %w", err)
	}

	if manifest.Guid == "" {
		return nil, fmt.Errorf("manifest.xml missing guid field")
	}

	cleanGuid := cleanString(manifest.Guid)
	cleanName := cleanString(manifest.Name)

	result := ModModel{
		cleanGuid: {
			Name:    cleanName,
			Version: manifest.Version,
			Path:    filePath,
		},
	}

	return result, nil
}

// ReadZipModBatch 批量解析 zipmod 文件（并发）
func ReadZipModBatch(filePaths []string, concurrency int) (ModModel, error) {
	if concurrency <= 0 {
		concurrency = 4
	}

	results := make(map[string]ModInfo)
	var mutex sync.Mutex

	type task struct {
		path  string
		index int
	}

	taskChan := make(chan task, len(filePaths))
	for i, path := range filePaths {
		taskChan <- task{path: path, index: i}
	}
	close(taskChan)

	var wg sync.WaitGroup
	for i := 0; i < concurrency; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for t := range taskChan {
				modModel, err := ReadZipMod(t.path)
				if err != nil {
					continue
				}

				mutex.Lock()
				for guid, info := range modModel {
					results[guid] = info
				}
				mutex.Unlock()
			}
		}()
	}

	wg.Wait()
	return results, nil
}
