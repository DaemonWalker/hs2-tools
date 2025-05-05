package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/PuerkitoBio/goquery"
)

const (
	FETCH_SIZE     = 1024
	MAX_PART_FETCH = 5
	MAX_RETRIES    = 3
	USER_AGENT     = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36 Edg/111.0.1661.54"
	GUID_START     = "<guid>"
	GUID_END       = "</guid>"
)

var (
	guidStartBytes = []byte(GUID_START)
	guidEndBytes   = []byte(GUID_END)
	urlQueue       = make([]string, 0)
	resultQueue    = make(chan []string, 10000)
	wg             sync.WaitGroup
)

// SideloadModel 定义 SideloadModel 类型
type SideloadModel map[string]string

// DownloadModel 定义 DownloadModel 类型
type DownloadModel struct {
	URL  string `json:"url"`
	Dir  string `json:"dir"`
	Guid string `json:"guid"`
}

// fetchWithRetry 带重试机制的 HTTP 请求
func fetchWithRetry(url string, method string, headers map[string]string, maxRetries int) (*http.Response, error) {
	// fmt.Printf("request: %s\n", url)
	var resp *http.Response
	var err error
	var req *http.Request
	for i := range maxRetries {
		req, err = http.NewRequest(method, url, nil)
		if err != nil {
			// fmt.Printf("NewRequest failed: %v\n", err)
			return nil, err
		}
		for k, v := range headers {
			req.Header.Set(k, v)
		}
		client := &http.Client{}
		resp, err = client.Do(req)
		if err == nil && resp.StatusCode >= 200 && resp.StatusCode < 300 {
			// fmt.Printf("response: %s\n", resp.Status)
			return resp, nil
		} else {
			if err == nil {
				err = errors.New("response status code: " + resp.Status)
				resp.Body.Close()
			}
			fmt.Printf("request %v failed: %v\n", url, err)
		}
		waitTime := time.Duration(100*time.Millisecond) * time.Duration(1<<i)
		time.Sleep(waitTime)
	}
	// fmt.Printf("fetchWithRetry failed: %v\n", err)
	return nil, fmt.Errorf("request %v failed retries: %v", url, err)
}

// checkBuffer 检查缓冲区是否包含 GUID 起始和结束标记
func checkBuffer(buffer []byte) bool {
	return bytes.Contains(buffer, guidStartBytes) && bytes.Contains(buffer, guidEndBytes)
}

// fetchRange 按范围获取文件内容
func fetchRange(url string, start, end int) ([]byte, error) {
	headers := map[string]string{
		"user-agent":      USER_AGENT,
		"accept":          "*/*",
		"accept-encoding": "identity",
		"range":           fmt.Sprintf("bytes=%d-%d", start, end),
		"host":            "sideload.betterrepack.com",
	}
	resp, err := fetchWithRetry(url, "GET", headers, MAX_RETRIES)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	return io.ReadAll(resp.Body)
}

// findGuid 查找文件中的 GUID
func findGuid(url string, size int) (string, error) {
	var leftBuffer, rightBuffer []byte
	var buffer []byte

	for i := range MAX_PART_FETCH {
		if size-(i+1)*FETCH_SIZE >= 0 {
			part, err := fetchRange(url, max(0, size-(i+1)*FETCH_SIZE), size-i*FETCH_SIZE-1)
			if err != nil {
				fmt.Printf("fetchRange failed: %v\n", err)
				return "", err
			}
			rightBuffer = append(part, rightBuffer...)
			if checkBuffer(rightBuffer) {
				buffer = rightBuffer
				break
			}
		}
		if i*FETCH_SIZE < size {
			part, err := fetchRange(url, i*FETCH_SIZE, min((i+1)*FETCH_SIZE-1, size-1))
			if err != nil {
				return "", err
			}
			leftBuffer = append(leftBuffer, part...)
			if checkBuffer(leftBuffer) {
				buffer = leftBuffer
				break
			}
		}
	}
	if buffer == nil {
		return "", nil
	}
	guidStart := bytes.Index(buffer, guidStartBytes)
	guidEnd := bytes.Index(buffer[guidStart:], guidEndBytes) + guidStart
	if guidStart == -1 || guidEnd == -1 {
		return "", nil
	}
	guid := buffer[guidStart+len(guidStartBytes) : guidEnd]
	return string(guid), nil
}

// getModGuid 获取模组的 GUID
func getModGuid(modUrl, baseUrl string) (SideloadModel, error) {
	// fmt.Printf("获取模组: %s\n", modUrl)
	headers := map[string]string{
		"user-agent":      USER_AGENT,
		"accept":          "*/*",
		"accept-encoding": "identity",
	}
	resp, err := fetchWithRetry(modUrl, "HEAD", headers, MAX_RETRIES)
	if err != nil {
		return nil, err
	}
	// fmt.Printf("resp :%v\n", resp == nil)
	// defer resp.Body.Close()
	contentLength := resp.Header.Get("Content-Length")
	if contentLength == "" {
		return nil, nil
	}
	size, err := strconv.Atoi(contentLength)
	if err != nil {
		return nil, err
	}
	guid, err := findGuid(modUrl, size)
	if err != nil {
		return nil, err
	}
	if guid == "" {
		return nil, nil
	}
	relUrl := strings.TrimPrefix(modUrl, baseUrl)
	return SideloadModel{guid: relUrl}, nil
}

// getAllMods 获取所有模组信息
func getAllMods(baseUrl string) {
	urlQueue = append(urlQueue, baseUrl)
	defer func() {
		close(resultQueue)
	}()
	for len(urlQueue) > 0 {
		fetchUrl := urlQueue[0]
		urlQueue = urlQueue[1:]
		fmt.Printf("%s 剩余:%d\n", fetchUrl, len(urlQueue))

		resp, err := fetchWithRetry(fetchUrl, "GET", map[string]string{"user-agent": USER_AGENT}, MAX_RETRIES)
		if err != nil {
			fmt.Printf("request failed: %v\n", err)
			urlQueue = append(urlQueue, fetchUrl)
			continue
		}

		doc, err := goquery.NewDocumentFromReader(resp.Body)
		if err != nil {
			fmt.Printf("parse failed: %v\n", err)
			continue
		}

		var hrefs []string
		doc.Find(".indexcolname a").Each(func(idx int, s *goquery.Selection) {
			if idx > 1 {
				if href, exists := s.Attr("href"); exists {
					decodedHref, err := url.QueryUnescape(href)
					if err == nil {
						hrefs = append(hrefs, fetchUrl+decodedHref)
					}
				}
			}
		})
		// fmt.Printf("hrefs: %v\n", hrefs)

		var dirs []string
		var zipmods []string
		for _, u := range hrefs {
			if strings.HasSuffix(u, ".zipmod") {
				zipmods = append(zipmods, u)
			} else {
				dirs = append(dirs, u)
			}
		}

		urlQueue = append(urlQueue, dirs...)

		if len(zipmods) > 0 {
			// 处理 zipmod 文件
			var modWg sync.WaitGroup
			for _, mod := range zipmods {
				modWg.Add(1)
				go func(mod string) {
					defer modWg.Done()
					modResult, err := getModGuid(mod, baseUrl)
					if err != nil {
						fmt.Println(err)
					}
					for k, v := range modResult {
						resultQueue <- []string{k, v}
					}
				}(mod)
			}
			modWg.Wait()
		}
	}
}

func consumer() {
	result := make(map[string]string)
	for zipmod := range resultQueue {
		guid := zipmod[0]
		modUrl := zipmod[1]
		result[guid] = modUrl
	}
	// 保存result到文件
	file, err := os.Create("result.json")
	if err != nil {
		fmt.Println(err)
	}
	defer func() {
		file.Close()
		wg.Done()
	}()

	encoder := json.NewEncoder(file)
	err = encoder.Encode(result)
	if err != nil {
		fmt.Println(err)
	}
}

func main() {
	// 示例调用 getAllMods
	baseUrl := "https://sideload.betterrepack.com/download/AISHS2/"

	go getAllMods(baseUrl)
	wg.Add(1)
	go consumer()
	wg.Wait()
}
