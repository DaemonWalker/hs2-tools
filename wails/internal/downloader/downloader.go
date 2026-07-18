package downloader

import (
	"context"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"time"
)

// ProgressMessage 进度消息结构
type ProgressMessage struct {
	Type       string  `json:"type"`       // progress, complete, error, info
	Downloaded int64   `json:"downloaded"` // 已下载字节数
	Total      int64   `json:"total"`      // 总字节数 (-1 表示未知)
	Speed      float64 `json:"speed"`      // 下载速度 (bytes/s)
	Percent    float64 `json:"percent"`    // 下载百分比
	Path       string  `json:"path"`       // 完成时的文件路径
	Message    string  `json:"message"`    // 错误或信息消息
}

// Downloader 下载器
type Downloader struct {
	client *http.Client
	ctx    context.Context
	cancel context.CancelFunc
}

// NewDownloader 创建新的下载器
func NewDownloader(proxyURL string) (*Downloader, error) {
	transport := &http.Transport{
		MaxIdleConns:        100,
		MaxIdleConnsPerHost: 10,
		IdleConnTimeout:     90 * time.Second,
	}

	if proxyURL != "" {
		proxy, err := url.Parse(proxyURL)
		if err != nil {
			return nil, fmt.Errorf("invalid proxy URL: %w", err)
		}
		transport.Proxy = http.ProxyURL(proxy)
	}

	ctx, cancel := context.WithCancel(context.Background())

	return &Downloader{
		client: &http.Client{
			Transport: transport,
			Timeout:   0,
		},
		ctx:    ctx,
		cancel: cancel,
	}, nil
}

// Cancel 取消下载
func (d *Downloader) Cancel() {
	d.cancel()
}

// Download 执行下载，通过 onProgress 回调推送进度
func (d *Downloader) Download(fileURL, outputPath string, resume bool, onProgress func(ProgressMessage)) error {
	// 确保输出目录存在
	dir := filepath.Dir(outputPath)
	if err := os.MkdirAll(dir, 0755); err != nil {
		return fmt.Errorf("failed to create directory: %w", err)
	}

	// 发送开始消息
	if onProgress != nil {
		onProgress(ProgressMessage{
			Type:    "info",
			Message: fmt.Sprintf("Starting download: %s", fileURL),
		})
	}

	// 检查是否已存在部分文件
	var startPos int64 = 0
	if resume {
		if info, err := os.Stat(outputPath); err == nil {
			startPos = info.Size()
			if onProgress != nil {
				onProgress(ProgressMessage{
					Type:    "info",
					Message: fmt.Sprintf("Resuming download from byte %d", startPos),
				})
			}
		}
	}

	// 创建 HTTP 请求
	req, err := http.NewRequestWithContext(d.ctx, "GET", fileURL, nil)
	if err != nil {
		return fmt.Errorf("failed to create request: %w", err)
	}

	// 设置断点续传头
	if startPos > 0 {
		req.Header.Set("Range", fmt.Sprintf("bytes=%d-", startPos))
	}

	// 设置请求头
	req.Header.Set("User-Agent", "hs2-tools-downloader/1.0")

	// 发送请求
	resp, err := d.client.Do(req)
	if err != nil {
		return fmt.Errorf("failed to send request: %w", err)
	}
	defer resp.Body.Close()

	// 检查响应状态
	if resp.StatusCode != http.StatusOK && resp.StatusCode != http.StatusPartialContent {
		return fmt.Errorf("server returned status %d: %s", resp.StatusCode, resp.Status)
	}

	// 获取文件总大小
	var totalSize int64 = -1
	if resp.StatusCode == http.StatusPartialContent {
		contentRange := resp.Header.Get("Content-Range")
		if contentRange != "" {
			totalSize = parseContentRangeTotal(contentRange)
		}
	} else {
		startPos = 0
		totalSize = resp.ContentLength
	}

	// 打开文件
	flag := os.O_CREATE | os.O_WRONLY
	if startPos > 0 {
		flag |= os.O_APPEND
	} else {
		flag |= os.O_TRUNC
	}

	file, err := os.OpenFile(outputPath, flag, 0644)
	if err != nil {
		return fmt.Errorf("failed to open file: %w", err)
	}
	defer file.Close()

	// 如果服务器不支持断点续传但文件已存在，重新开始下载
	if startPos > 0 && resp.StatusCode == http.StatusOK {
		if onProgress != nil {
			onProgress(ProgressMessage{
				Type:    "info",
				Message: "Server does not support resume, restarting download",
			})
		}
		startPos = 0
		if err := file.Truncate(0); err != nil {
			return fmt.Errorf("failed to truncate file: %w", err)
		}
		if _, err := file.Seek(0, 0); err != nil {
			return fmt.Errorf("failed to seek file: %w", err)
		}
	}

	// 创建进度追踪器
	tracker := &progressTracker{
		startPos:       startPos,
		total:          totalSize,
		startTime:      time.Now(),
		lastUpdate:     time.Now(),
		lastDownloaded: 0,
		onProgress:     onProgress,
	}

	// 使用带取消功能的 Reader
	reader := &cancelableReader{
		Reader: resp.Body,
		ctx:    d.ctx,
	}

	// 创建缓冲写入
	buf := make([]byte, 32*1024) // 32KB buffer
	for {
		n, err := reader.Read(buf)
		if n > 0 {
			if _, werr := file.Write(buf[:n]); werr != nil {
				return fmt.Errorf("failed to write file: %w", werr)
			}
			tracker.update(int64(n))
		}
		if err != nil {
			if err == io.EOF {
				break
			}
			if d.ctx.Err() == context.Canceled {
				return fmt.Errorf("download cancelled")
			}
			return fmt.Errorf("failed to read response: %w", err)
		}
	}

	// 发送完成消息
	if onProgress != nil {
		onProgress(ProgressMessage{
			Type:    "complete",
			Path:    outputPath,
			Total:   tracker.current(),
			Message: "Download completed successfully",
		})
	}

	return nil
}

// parseContentRangeTotal 解析 Content-Range 头获取总大小
func parseContentRangeTotal(contentRange string) int64 {
	var total int64 = -1
	fmt.Sscanf(contentRange, "bytes %*d-%*d/%d", &total)
	return total
}

// progressTracker 进度追踪器
type progressTracker struct {
	startPos         int64
	downloaded       int64
	total            int64
	startTime        time.Time
	lastUpdate       time.Time
	lastDownloaded   int64
	onProgress       func(ProgressMessage)
}

func (p *progressTracker) current() int64 {
	return p.startPos + p.downloaded
}

func (p *progressTracker) update(n int64) {
	p.downloaded += n
	now := time.Now()

	// 每 200ms 或每下载 64KB 更新一次进度
	if now.Sub(p.lastUpdate) > 200*time.Millisecond || p.downloaded-p.lastDownloaded > 64*1024 {
		p.sendProgress()
		p.lastUpdate = now
		p.lastDownloaded = p.downloaded
	}
}

func (p *progressTracker) sendProgress() {
	current := p.current()
	elapsed := time.Since(p.startTime).Seconds()

	var speed float64 = 0
	if elapsed > 0 {
		speed = float64(p.downloaded) / elapsed
	}

	var percent float64 = 0
	if p.total > 0 {
		percent = float64(current) / float64(p.total) * 100
	}

	if p.onProgress != nil {
		p.onProgress(ProgressMessage{
			Type:       "progress",
			Downloaded: current,
			Total:      p.total,
			Speed:      speed,
			Percent:    percent,
		})
	}
}

// cancelableReader 支持取消的 Reader
type cancelableReader struct {
	io.Reader
	ctx context.Context
}

func (r *cancelableReader) Read(p []byte) (int, error) {
	select {
	case <-r.ctx.Done():
		return 0, r.ctx.Err()
	default:
		return r.Reader.Read(p)
	}
}
