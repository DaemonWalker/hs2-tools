package sideloader

import (
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/PuerkitoBio/goquery"
)

const userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36 Edg/111.0.1661.54"

// Sideloader 爬虫
type Sideloader struct {
	proxy       string
	cancelled   bool
	mu          sync.Mutex
	httpClient  *http.Client
	baseURL     string
	result      map[string]string
	resultMu    sync.Mutex
	modSem      chan struct{}
}

// NewSideloader 创建新的 Sideloader
func NewSideloader(proxy string) *Sideloader {
	transport := &http.Transport{
		MaxIdleConns:        50,
		MaxIdleConnsPerHost: 10,
		IdleConnTimeout:     90 * time.Second,
	}
	if proxy != "" {
		p, _ := url.Parse(proxy)
		transport.Proxy = http.ProxyURL(p)
	}

	return &Sideloader{
		proxy:      proxy,
		httpClient: &http.Client{Transport: transport, Timeout: 60 * time.Second},
		result:     make(map[string]string),
	}
}

// Cancel 取消爬取
func (s *Sideloader) Cancel() {
	s.mu.Lock()
	s.cancelled = true
	s.mu.Unlock()
}

// isCancelled 检查是否已取消
func (s *Sideloader) isCancelled() bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.cancelled
}

// Run 运行爬取
func (s *Sideloader) Run(
	onLog func(string),
	onProgress func(current, total int),
) (map[string]string, error) {
	startURL := "https://sideload.betterrepack.com/download/AISHS2/"
	s.baseURL = startURL
	s.modSem = make(chan struct{}, 10)
	s.cancelled = false

	if onLog != nil {
		onLog(fmt.Sprintf("Starting crawl from %s", startURL))
	}

	s.crawl(startURL, onLog, onProgress)

	if onLog != nil {
		onLog(fmt.Sprintf("Crawl completed, found %d mods", len(s.result)))
	}

	return s.result, nil
}

func (s *Sideloader) fetchDoc(pageURL string) (*goquery.Document, error) {
	req, err := http.NewRequest("GET", pageURL, nil)
	if err != nil {
		return nil, err
	}
	req.Header.Set("User-Agent", userAgent)
	resp, err := s.httpClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("HTTP %d", resp.StatusCode)
	}
	return goquery.NewDocumentFromReader(resp.Body)
}

func (s *Sideloader) fetchRange(modURL string, start, end int64) ([]byte, error) {
	req, _ := http.NewRequest("GET", modURL, nil)
	req.Header.Set("User-Agent", userAgent)
	req.Header.Set("Range", fmt.Sprintf("bytes=%d-%d", start, end))
	req.Header.Set("Accept-Encoding", "identity")
	resp, err := s.httpClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusPartialContent && resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("HTTP %d", resp.StatusCode)
	}
	return io.ReadAll(resp.Body)
}

func (s *Sideloader) getFileSize(modURL string) (int64, error) {
	req, _ := http.NewRequest("HEAD", modURL, nil)
	req.Header.Set("User-Agent", userAgent)
	resp, err := s.httpClient.Do(req)
	if err != nil {
		return 0, err
	}
	resp.Body.Close()
	return strconv.ParseInt(resp.Header.Get("Content-Length"), 10, 64)
}

func (s *Sideloader) parseLinks(doc *goquery.Document, pageURL string) (dirs, mods []string) {
	hrefMap := make(map[string]bool)
	doc.Find("table#indexlist tr").Each(func(i int, sel *goquery.Selection) {
		if i <= 1 {
			return
		}
		sel.Find("a").Each(func(_ int, a *goquery.Selection) {
			if href, ok := a.Attr("href"); ok {
				if decoded, err := url.QueryUnescape(href); err == nil && decoded != "" && decoded != "/" {
					hrefMap[pageURL+decoded] = true
				}
			}
		})
	})
	for href := range hrefMap {
		if strings.HasSuffix(strings.ToLower(href), ".zipmod") {
			mods = append(mods, href)
		} else if strings.HasSuffix(href, "/") {
			dirs = append(dirs, href)
		}
	}
	return dirs, mods
}

func (s *Sideloader) extractGUIDFromZipmod(modURL string) string {
	size, err := s.getFileSize(modURL)
	if err != nil {
		return ""
	}

	var data []byte
	if size <= 65536 {
		data, _ = s.fetchRange(modURL, 0, size-1)
	} else {
		const chunkSize = int64(16384)
		for offset := int64(0); offset < 262144 && offset < size; offset += chunkSize {
			end := size - offset - 1
			start := end - chunkSize + 1
			if start < 0 {
				start = 0
			}
			chunk, _ := s.fetchRange(modURL, start, end)
			data = append(chunk, data...)
			if entries := readCentralDir(data, size); entries != nil {
				return s.extractFromEntries(entries, modURL)
			}
		}
	}
	if len(data) == 0 {
		return ""
	}
	if entries := readCentralDir(data, size); entries != nil {
		return s.extractFromEntries(entries, modURL)
	}
	return ""
}

func (s *Sideloader) extractFromEntries(entries map[string]fileEntry, modURL string) string {
	var entry *fileEntry
	for name, e := range entries {
		if strings.EqualFold(name, "manifest.xml") {
			entry = &e
			break
		}
	}
	if entry == nil {
		return ""
	}

	headerData, _ := s.fetchRange(modURL, int64(entry.offset), int64(entry.offset)+200)
	dataOffset, compressedSize, err := parseLocalHeader(headerData)
	if err != nil {
		dataOffset = int64(30 + len(entry.name))
		compressedSize = entry.compressedSize
	}

	actualOffset := int64(entry.offset) + dataOffset
	manifestData, _ := s.fetchRange(modURL, actualOffset, actualOffset+int64(compressedSize)-1)
	guid, _ := extractManifestXML(manifestData, entry.compressionMethod)
	return guid
}

func (s *Sideloader) crawl(pageURL string, onLog func(string), onProgress func(current, total int)) {
	if s.isCancelled() {
		return
	}

	if onLog != nil {
		onLog(fmt.Sprintf("Processing: %s", pageURL))
	}

	doc, err := s.fetchDoc(pageURL)
	if err != nil {
		return
	}
	dirs, mods := s.parseLinks(doc, pageURL)

	var wg sync.WaitGroup
	for _, mod := range mods {
		if s.isCancelled() {
			break
		}
		wg.Add(1)
		s.modSem <- struct{}{}
		go func(modURL string) {
			defer wg.Done()
			defer func() { <-s.modSem }()
			if guid := s.extractGUIDFromZipmod(modURL); guid != "" {
				s.resultMu.Lock()
				s.result[guid] = strings.TrimPrefix(modURL, s.baseURL)
				s.resultMu.Unlock()
				if onLog != nil {
					onLog(fmt.Sprintf("Found: %s", guid))
				}
			}
			if onProgress != nil {
				s.resultMu.Lock()
				current := len(s.result)
				s.resultMu.Unlock()
				onProgress(current, 0)
			}
		}(mod)
	}
	wg.Wait()

	dirSem := make(chan struct{}, 3)
	var dirWg sync.WaitGroup
	for _, dir := range dirs {
		if s.isCancelled() {
			break
		}
		dirWg.Add(1)
		dirSem <- struct{}{}
		go func(dirURL string) {
			defer dirWg.Done()
			defer func() { <-dirSem }()
			s.crawl(dirURL, onLog, onProgress)
		}(dir)
	}
	dirWg.Wait()
}
