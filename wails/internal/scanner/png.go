package scanner

import (
	"bytes"
	"encoding/base64"
	"fmt"
	"os"
	"strings"
	"sync"
)

var (
	// 标记定义
	nameStart = []byte("fullname")
	nameEnd   = []byte("personality")
	modStart  = []byte("ModID")
	modEnd    = []byte("Slot")
)

// ReadPngMods 从 PNG 文件中提取所有 Mod GUID
func ReadPngMods(filePath string) ([]string, error) {
	exists, _ := FileExists(filePath)
	if !exists {
		return []string{}, nil
	}

	data, err := os.ReadFile(filePath)
	if err != nil {
		return nil, err
	}

	return searchBuffer(modStart, modEnd, data), nil
}

// ReadPngNames 从 PNG 文件中提取所有角色名称
func ReadPngNames(filePath string) ([]string, error) {
	exists, _ := FileExists(filePath)
	if !exists {
		return []string{}, nil
	}

	data, err := os.ReadFile(filePath)
	if err != nil {
		return nil, err
	}

	return searchBuffer(nameStart, nameEnd, data), nil
}

// searchBuffer 从 Buffer 中循环提取所有 [start...end] 区间的字符串
func searchBuffer(start, end, data []byte) []string {
	result := make(map[string]bool) // 使用 map 去重
	startLen := len(start)
	endLen := len(end)

	searchData := data
	for {
		startIndex := bytes.Index(searchData, start)
		if startIndex == -1 {
			break
		}

		// 从 startIndex 之后开始搜索 end
		endIndex := bytes.Index(searchData[startIndex+startLen:], end)
		if endIndex == -1 {
			break
		}
		// 计算相对于原始 searchData 的绝对位置
		endIndex += startIndex + startLen

		// 提取内容（去掉首尾字节）
		content := searchData[startIndex+startLen : endIndex]
		if len(content) > 2 {
			content = content[1 : len(content)-1] // 去掉第一个和最后一个字节
		}
		str := bufferToString(content)
		if str != "" {
			result[str] = true
		}

		// 移动到 end 之后继续搜索
		searchData = searchData[endIndex+endLen:]
	}

	// 转换为切片（确保返回非 nil 的空数组）
	list := make([]string, 0, len(result))
	for str := range result {
		list = append(list, str)
	}
	return list
}

// bufferToString 将 Buffer 转为字符串
func bufferToString(buffer []byte) string {
	return strings.TrimSpace(string(buffer))
}

// ParsePngData 解析 PNG 文件的完整数据（用于调试）
func ParsePngData(filePath string) (*PngParseResult, error) {
	exists, _ := FileExists(filePath)
	if !exists {
		return nil, fmt.Errorf("not a valid PNG file")
	}

	data, err := os.ReadFile(filePath)
	if err != nil {
		return nil, err
	}

	// 查找 IEND 位置
	pngEnd := []byte("IEND")
	iendIndex := -1
	for i := len(data) - 1; i >= 3; i-- {
		if data[i] == pngEnd[3] && data[i-1] == pngEnd[2] &&
			data[i-2] == pngEnd[1] && data[i-3] == pngEnd[0] {
			iendIndex = i + 1
			break
		}
	}

	if iendIndex == -1 {
		return nil, fmt.Errorf("IEND marker not found")
	}

	// 提取游戏数据区（IEND 之后）
	gameData := data[iendIndex:]

	mods := searchBuffer(modStart, modEnd, data)
	names := searchBuffer(nameStart, nameEnd, data)

	return &PngParseResult{
		ModIDs:      mods,
		CharaNames:  names,
		GameDataLen: len(gameData),
	}, nil
}

// ReadPngModsBatch 批量从 PNG 文件中提取 Mod GUID
func ReadPngModsBatch(filePaths []string, concurrency int) ([]PngModResult, error) {
	if concurrency <= 0 {
		concurrency = 8
	}

	results := make([]PngModResult, 0, len(filePaths))
	var mutex sync.Mutex

	taskChan := make(chan string, len(filePaths))
	for _, path := range filePaths {
		taskChan <- path
	}
	close(taskChan)

	var wg sync.WaitGroup
	for i := 0; i < concurrency; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for path := range taskChan {
				modIDs, err := ReadPngMods(path)
				if err != nil {
					continue
				}

				mutex.Lock()
				if modIDs == nil {
					modIDs = []string{}
				}
				results = append(results, PngModResult{
					Path:   path,
					ModIDs: modIDs,
				})
				mutex.Unlock()
			}
		}()
	}

	wg.Wait()
	return results, nil
}

// ReadPngNamesBatch 批量从 PNG 文件中提取角色名称
func ReadPngNamesBatch(filePaths []string, concurrency int) ([]PngNamesResult, error) {
	if concurrency <= 0 {
		concurrency = 8
	}

	results := make([]PngNamesResult, 0, len(filePaths))
	var mutex sync.Mutex

	taskChan := make(chan string, len(filePaths))
	for _, path := range filePaths {
		taskChan <- path
	}
	close(taskChan)

	var wg sync.WaitGroup
	for i := 0; i < concurrency; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for path := range taskChan {
				names, err := ReadPngNames(path)
				if err != nil {
					continue
				}

				mutex.Lock()
				if names == nil {
					names = []string{}
				}
				results = append(results, PngNamesResult{
					Path:  path,
					Names: names,
				})
				mutex.Unlock()
			}
		}()
	}

	wg.Wait()
	return results, nil
}

// ReadPngImage 读取单个 PNG 文件的缩略图（Base64）
func ReadPngImage(filePath string) (string, error) {
	exists, _ := FileExists(filePath)
	if !exists {
		return "", nil
	}

	data, err := os.ReadFile(filePath)
	if err != nil {
		return "", nil
	}

	// 查找 IEND 标记位置
	pngEnd := []byte("IEND")
	endIndex := -1
	for i := len(data) - 1; i >= 3; i-- {
		if data[i] == pngEnd[3] && data[i-1] == pngEnd[2] &&
			data[i-2] == pngEnd[1] && data[i-3] == pngEnd[0] {
			endIndex = i + 1
			break
		}
	}

	if endIndex == -1 || endIndex > len(data) {
		return "", nil
	}

	// 截取纯 PNG 图像数据
	pngData := data[:endIndex]
	return base64.StdEncoding.EncodeToString(pngData), nil
}

// ReadPngImagesBatch 批量从 PNG 文件中提取缩略图（Base64）
func ReadPngImagesBatch(filePaths []string, concurrency int) ([]PngImageResult, error) {
	if concurrency <= 0 {
		concurrency = 4 // 缩略图读取较耗费资源，默认并发数较低
	}

	results := make([]PngImageResult, 0, len(filePaths))
	var mutex sync.Mutex

	taskChan := make(chan string, len(filePaths))
	for _, path := range filePaths {
		taskChan <- path
	}
	close(taskChan)

	var wg sync.WaitGroup
	for i := 0; i < concurrency; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for path := range taskChan {
				imageData, err := ReadPngImage(path)
				if err != nil {
					continue
				}

				mutex.Lock()
				results = append(results, PngImageResult{
					Path:      path,
					ImageData: imageData,
				})
				mutex.Unlock()
			}
		}()
	}

	wg.Wait()
	return results, nil
}

// ReadPngPageDataBatch 批量获取页面数据（名称 + 缩略图）
func ReadPngPageDataBatch(filePaths []string, concurrency int) ([]PngPageDataResult, error) {
	if concurrency <= 0 {
		concurrency = 4
	}

	results := make([]PngPageDataResult, 0, len(filePaths))
	var mutex sync.Mutex

	taskChan := make(chan string, len(filePaths))
	for _, path := range filePaths {
		taskChan <- path
	}
	close(taskChan)

	var wg sync.WaitGroup
	for i := 0; i < concurrency; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for path := range taskChan {
				names, nameErr := ReadPngNames(path)
				imageData, imageErr := ReadPngImage(path)

				if nameErr != nil {
					continue
				}

				if names == nil {
					names = []string{}
				}

				if imageErr != nil {
					imageData = ""
				}

				mutex.Lock()
				results = append(results, PngPageDataResult{
					Path:      path,
					Names:     names,
					ImageData: imageData,
				})
				mutex.Unlock()
			}
		}()
	}

	wg.Wait()
	return results, nil
}
