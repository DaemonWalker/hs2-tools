package main

import (
	"context"
	"embed"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"sync"
	"time"

	wailsRuntime "github.com/wailsapp/wails/v2/pkg/runtime"
	"wails/internal/downloader"
	"wails/internal/scanner"
	"wails/internal/sideloader"
	"wails/internal/utils"
)

//go:embed resources/sideload.zip
var sideloadZip embed.FS

// App struct
type App struct {
	ctx               context.Context
	proxy             string
	proxyAuth         string
	activeDownloads   map[string]*downloader.Downloader
	downloadsMu       sync.RWMutex
	sideloader        *sideloader.Sideloader
	sideloaderRunning bool
	sideloaderMu      sync.RWMutex
}

// NewApp creates a new App application struct
func NewApp() *App {
	return &App{
		activeDownloads: make(map[string]*downloader.Downloader),
	}
}

// startup is called when the app starts
func (a *App) startup(ctx context.Context) {
	a.ctx = ctx
}

// ==================== 基础文件操作 ====================

// ReadDir 读取目录内容
func (a *App) ReadDir(dirPath string) ([]string, error) {
	entries, err := os.ReadDir(dirPath)
	if err != nil {
		return nil, err
	}
	var names []string
	for _, entry := range entries {
		names = append(names, entry.Name())
	}
	return names, nil
}

// SelectPath 选择游戏 exe 路径
func (a *App) SelectPath() (string, error) {
	selection, err := wailsRuntime.OpenFileDialog(a.ctx, wailsRuntime.OpenDialogOptions{
		Filters: []wailsRuntime.FileFilter{
			{DisplayName: "*.exe", Pattern: "*.exe"},
		},
	})
	if err != nil {
		return "", err
	}
	// 去掉 exe 名称，返回目录
	if selection != "" {
		selection = filepath.Dir(selection)
	}
	return selection, nil
}

// OpenFileSelector 选择 PNG 文件
func (a *App) OpenFileSelector(defaultPath string) (string, error) {
	selection, err := wailsRuntime.OpenFileDialog(a.ctx, wailsRuntime.OpenDialogOptions{
		DefaultDirectory: filepath.Dir(defaultPath),
		Filters: []wailsRuntime.FileFilter{
			{DisplayName: "*.png", Pattern: "*.png"},
		},
	})
	return selection, err
}

// FileExists 检查文件是否存在且是 PNG
func (a *App) FileExists(filePath string) bool {
	exists, _ := scanner.FileExists(filePath)
	return exists
}

// DirExists 检查文件夹是否存在
func (a *App) DirExists(dirPath string) bool {
	info, err := os.Stat(dirPath)
	return err == nil && info.IsDir()
}

// ==================== Scanner 相关 ====================

// ReadPngForMod 从 PNG 中提取 Mod GUID 列表
func (a *App) ReadPngForMod(path string) ([]string, error) {
	return scanner.ReadPngMods(path)
}

// ReadAllCharaNames 从 PNG 中提取角色名称列表
func (a *App) ReadAllCharaNames(path string) ([]string, error) {
	return scanner.ReadPngNames(path)
}

// ReadPngForShow 从 PNG 中提取图像数据（Base64）
func (a *App) ReadPngForShow(path string) (string, error) {
	return scanner.ReadPngImage(path)
}

// GetAllFiles 递归扫描目录
func (a *App) GetAllFiles(path string, options *scanner.Options) ([]string, error) {
	return scanner.ScanDirectory(path, options)
}

// ReadZipMod 解析 zipmod 文件
func (a *App) ReadZipMod(filePath string) (scanner.ModModel, error) {
	return scanner.ReadZipMod(filePath)
}

// ReadZipModBatch 批量解析 zipmod 文件
func (a *App) ReadZipModBatch(filePaths []string) (scanner.ModModel, error) {
	return scanner.ReadZipModBatch(filePaths, 8)
}

// ReadPngModsBatch 批量从 PNG 中提取 Mod GUID
func (a *App) ReadPngModsBatch(filePaths []string) ([]scanner.PngModResult, error) {
	return scanner.ReadPngModsBatch(filePaths, 8)
}

// ReadPngNamesBatch 批量从 PNG 中提取角色名称
func (a *App) ReadPngNamesBatch(filePaths []string) ([]scanner.PngNamesResult, error) {
	return scanner.ReadPngNamesBatch(filePaths, 8)
}

// ReadPngImagesBatch 批量从 PNG 中提取缩略图
func (a *App) ReadPngImagesBatch(filePaths []string) ([]scanner.PngImageResult, error) {
	return scanner.ReadPngImagesBatch(filePaths, 4)
}

// ReadPngPageDataBatch 批量获取页面数据（名称+缩略图）
func (a *App) ReadPngPageDataBatch(filePaths []string) ([]scanner.PngPageDataResult, error) {
	return scanner.ReadPngPageDataBatch(filePaths, 4)
}

// MoveFile 移动文件
func (a *App) MoveFile(file string, target string) error {
	return scanner.MoveFile(file, target)
}

// CheckTargetDir 检查并创建目标目录
func (a *App) CheckTargetDir(target string) error {
	return scanner.CheckTargetDir(target)
}

// ==================== 配置读写 ====================

// LoadSettings 加载设置
func (a *App) LoadSettings() (map[string]interface{}, error) {
	return utils.LoadSettings()
}

// SaveSettings 保存设置
func (a *App) SaveSettings(settings map[string]interface{}) error {
	return utils.SaveSettings(settings)
}

// LoadLocalMods 加载本地 Mod 数据
func (a *App) LoadLocalMods() (map[string]interface{}, error) {
	return utils.LoadLocalMods()
}

// SaveLocalMods 保存本地 Mod 数据
func (a *App) SaveLocalMods(mods map[string]interface{}) error {
	return utils.SaveLocalMods(mods)
}

// ==================== 代理设置 ====================

// ProxyInfo 代理信息
type ProxyInfo struct {
	URI      string `json:"uri"`
	Username string `json:"username"`
	Password string `json:"password"`
}

// SetProxy 设置代理
func (a *App) SetProxy(proxy ProxyInfo) {
	a.proxy = proxy.URI
	if proxy.Username != "" && proxy.Password != "" {
		a.proxyAuth = proxy.Username + ":" + proxy.Password
	} else {
		a.proxyAuth = ""
	}
}

// getProxyString 获取完整的代理字符串（含认证）
func (a *App) getProxyString() string {
	if a.proxyAuth != "" && a.proxy != "" {
		// 替换 protocol:// 为 protocol://user:pass@
		for _, proto := range []string{"http://", "https://", "socks5://"} {
			if len(a.proxy) > len(proto) && a.proxy[:len(proto)] == proto {
				return proto + a.proxyAuth + "@" + a.proxy[len(proto):]
			}
		}
	}
	return a.proxy
}

// ==================== 下载功能 ====================

// DownloadModel 下载信息
type DownloadModel struct {
	Name string `json:"name"`
	URL  string `json:"url"`
	Dir  string `json:"dir"`
}

// DownloadProgressEvent 下载进度事件
type DownloadProgressEvent struct {
	GUID       string  `json:"guid"`
	Type       string  `json:"type"`
	Downloaded int64   `json:"downloaded"`
	Total      int64   `json:"total"`
	Speed      float64 `json:"speed"`
	Percent    float64 `json:"percent"`
	Path       string  `json:"path"`
	Message    string  `json:"message"`
}

// DownloadCompleteEvent 下载完成事件
type DownloadCompleteEvent struct {
	GUID    string `json:"guid"`
	Success bool   `json:"success"`
	Message string `json:"message"`
}

// ScannerStatus Scanner 状态
type ScannerStatus struct {
	ScannerAvailable bool   `json:"scannerAvailable"`
	ScannerPath      string `json:"scannerPath"`
	Version          string `json:"version"`
}

// DownloaderStatus 下载器状态
type DownloaderStatus struct {
	GoDownloaderAvailable bool   `json:"goDownloaderAvailable"`
	GoDownloaderPath      string `json:"goDownloaderPath"`
}

// TriggerDownload 触发下载
func (a *App) TriggerDownload(info DownloadModel) error {
	downloadURL := "https://sideload.betterrepack.com/download/AISHS2/" + info.URL
	outputPath := filepath.Join(info.Dir, info.Name+".zipmod")

	// 确保目录存在
	if err := os.MkdirAll(info.Dir, 0755); err != nil {
		return fmt.Errorf("failed to create download directory: %w", err)
	}

	proxyString := a.getProxyString()
	dl, err := downloader.NewDownloader(proxyString)
	if err != nil {
		return fmt.Errorf("failed to create downloader: %w", err)
	}

	a.downloadsMu.Lock()
	a.activeDownloads[info.Name] = dl
	a.downloadsMu.Unlock()

	go func() {
		defer func() {
			a.downloadsMu.Lock()
			delete(a.activeDownloads, info.Name)
			a.downloadsMu.Unlock()
		}()

		err := dl.Download(downloadURL, outputPath, true, func(p downloader.ProgressMessage) {
			wailsRuntime.EventsEmit(a.ctx, "download:progress", DownloadProgressEvent{
				GUID:       info.Name,
				Type:       p.Type,
				Downloaded: p.Downloaded,
				Total:      p.Total,
				Speed:      p.Speed,
				Percent:    p.Percent,
				Path:       p.Path,
				Message:    p.Message,
			})

			if p.Type == "complete" {
				wailsRuntime.EventsEmit(a.ctx, "download:complete", DownloadCompleteEvent{
					GUID:    info.Name,
					Success: true,
					Message: "Download completed",
				})
			} else if p.Type == "error" {
				wailsRuntime.EventsEmit(a.ctx, "download:complete", DownloadCompleteEvent{
					GUID:    info.Name,
					Success: false,
					Message: p.Message,
				})
			}
		})

		if err != nil {
			wailsRuntime.EventsEmit(a.ctx, "download:complete", DownloadCompleteEvent{
				GUID:    info.Name,
				Success: false,
				Message: err.Error(),
			})
		}
	}()

	return nil
}

// CancelDownload 取消下载
func (a *App) CancelDownload(guid string) bool {
	a.downloadsMu.Lock()
	defer a.downloadsMu.Unlock()
	if dl, ok := a.activeDownloads[guid]; ok {
		dl.Cancel()
		return true
	}
	return false
}

// GetScannerStatus 获取 Scanner 状态
func (a *App) GetScannerStatus() ScannerStatus {
	return ScannerStatus{
		ScannerAvailable: true,
		ScannerPath:      "embedded",
		Version:          "2.0",
	}
}

// GetDownloaderStatus 获取下载器状态
func (a *App) GetDownloaderStatus() DownloaderStatus {
	return DownloaderStatus{
		GoDownloaderAvailable: true,
		GoDownloaderPath:      "embedded",
	}
}

// ==================== Sideloader 功能 ====================

// SideloaderProgressEvent Sideloader 进度事件
type SideloaderProgressEvent struct {
	Type    string `json:"type"`
	Message string `json:"message"`
	Current int    `json:"current"`
	Total   int    `json:"total"`
}

// SideloaderCompleteEvent Sideloader 完成事件
type SideloaderCompleteEvent struct {
	Success bool                   `json:"success"`
	Data    map[string]interface{} `json:"data"`
	Error   string                 `json:"error"`
}

// SideloaderStatus Sideloader 状态
type SideloaderStatus struct {
	SideloaderAvailable bool   `json:"sideloaderAvailable"`
	SideloaderPath      string `json:"sideloaderPath"`
	Version             string `json:"version"`
}

// RunSideloader 运行 Sideloader 更新
func (a *App) RunSideloader() error {
	a.sideloaderMu.Lock()
	if a.sideloaderRunning {
		a.sideloaderMu.Unlock()
		return fmt.Errorf("sideloader is already running")
	}
	a.sideloaderRunning = true
	proxyString := a.getProxyString()
	a.sideloader = sideloader.NewSideloader(proxyString)
	a.sideloaderMu.Unlock()

	go func() {
		defer func() {
			a.sideloaderMu.Lock()
			a.sideloaderRunning = false
			a.sideloaderMu.Unlock()
		}()

		result, err := a.sideloader.Run(
			func(msg string) {
				wailsRuntime.EventsEmit(a.ctx, "sideloader:progress", SideloaderProgressEvent{
					Type:    "info",
					Message: msg,
				})
			},
			func(current, total int) {
				wailsRuntime.EventsEmit(a.ctx, "sideloader:progress", SideloaderProgressEvent{
					Type:    "progress",
					Current: current,
					Total:   total,
				})
			},
		)

		if err != nil {
			dataMap := make(map[string]interface{})
			for k, v := range result {
				dataMap[k] = v
			}
			wailsRuntime.EventsEmit(a.ctx, "sideloader:complete", SideloaderCompleteEvent{
				Success: false,
				Data:    dataMap,
				Error:   err.Error(),
			})
		} else {
			dataMap := make(map[string]interface{})
			for k, v := range result {
				dataMap[k] = v
			}
			wailsRuntime.EventsEmit(a.ctx, "sideloader:complete", SideloaderCompleteEvent{
				Success: true,
				Data:    dataMap,
			})
		}
	}()

	return nil
}

// StopSideloader 停止 Sideloader
func (a *App) StopSideloader() bool {
	a.sideloaderMu.Lock()
	defer a.sideloaderMu.Unlock()
	if a.sideloader != nil {
		a.sideloader.Cancel()
		return true
	}
	return false
}

// IsSideloaderRunning 检查 Sideloader 是否正在运行
func (a *App) IsSideloaderRunning() bool {
	a.sideloaderMu.RLock()
	defer a.sideloaderMu.RUnlock()
	return a.sideloaderRunning
}

// GetSideloaderStatus 获取 Sideloader 状态
func (a *App) GetSideloaderStatus() SideloaderStatus {
	return SideloaderStatus{
		SideloaderAvailable: true,
		SideloaderPath:      "embedded",
		Version:             "2.0",
	}
}

// ==================== 系统功能 ====================

// InitSideload 加载内置 sideload 数据
func (a *App) InitSideload() (map[string]interface{}, error) {
	// 读取嵌入的 zip 文件到临时文件
	data, err := sideloadZip.ReadFile("resources/sideload.zip")
	if err != nil {
		return nil, fmt.Errorf("failed to read embedded sideload.zip: %w", err)
	}

	// 写入临时文件
	tmpFile := filepath.Join(os.TempDir(), "sideload.zip")
	if err := os.WriteFile(tmpFile, data, 0644); err != nil {
		return nil, fmt.Errorf("failed to write temp file: %w", err)
	}
	defer os.Remove(tmpFile)

	// 解压并读取 sideload.json
	result, err := utils.ExtractZipJSON(tmpFile, "sideload.json")
	if err != nil {
		return nil, fmt.Errorf("failed to extract sideload.json: %w", err)
	}

	return result, nil
}

// DisableWindowsSleep 阻止 Windows 休眠
func (a *App) DisableWindowsSleep() int {
	// Windows 下使用 powercfg 或 SetThreadExecutionState
	if runtime.GOOS == "windows" {
		go func() {
			ticker := time.NewTicker(30 * time.Second)
			defer ticker.Stop()
			for range ticker.C {
				// 简单的防止休眠：移动鼠标
				// 更好的方案是调用 Windows API SetThreadExecutionState
				exec.Command("powershell", "-Command", "$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys('{F15}')").Run()
			}
		}()
	}
	return 1
}

// EnableWindowsSleep 恢复 Windows 休眠
func (a *App) EnableWindowsSleep(id int) {
	// 简化实现，实际在 DisableWindowsSleep 中启动的 goroutine 会在应用退出时自动停止
	_ = id
}

// LaunchGame 启动游戏
func (a *App) LaunchGame() error {
	settings, err := utils.LoadSettings()
	if err != nil || settings == nil {
		return fmt.Errorf("游戏路径未设置")
	}
	gamePath, ok := settings["path"].(string)
	if !ok || gamePath == "" {
		return fmt.Errorf("游戏路径未设置")
	}
	exePath := filepath.Join(gamePath, "HoneySelect2.exe")
	cmd := exec.Command(exePath)
	cmd.Dir = gamePath
	cmd.Stdout = nil
	cmd.Stderr = nil
	return cmd.Start()
}

// LaunchStudio 启动工作室
func (a *App) LaunchStudio() error {
	settings, err := utils.LoadSettings()
	if err != nil || settings == nil {
		return fmt.Errorf("游戏路径未设置")
	}
	gamePath, ok := settings["path"].(string)
	if !ok || gamePath == "" {
		return fmt.Errorf("游戏路径未设置")
	}
	exePath := filepath.Join(gamePath, "StudioNEOV2.exe")
	cmd := exec.Command(exePath)
	cmd.Dir = gamePath
	cmd.Stdout = nil
	cmd.Stderr = nil
	return cmd.Start()
}

// OpenInFolder 在文件管理器中显示文件
func (a *App) OpenInFolder(filePath string) error {
	var cmd *exec.Cmd
	switch runtime.GOOS {
	case "windows":
		cmd = exec.Command("explorer", "/select,", filePath)
	case "darwin":
		cmd = exec.Command("open", "-R", filePath)
	default:
		cmd = exec.Command("xdg-open", filepath.Dir(filePath))
	}
	return cmd.Start()
}

// Log 日志输出
func (a *App) Log(data ...interface{}) {
	fmt.Println(data...)
}

// Ping 测试连通性
func (a *App) Ping() string {
	return "pong"
}
