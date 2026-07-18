package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"syscall"
	"time"

	"wails/internal/downloader"
)

func main() {
	var (
		url     = flag.String("url", "", "Download URL (required)")
		output  = flag.String("out", "", "Output file path (required)")
		proxy   = flag.String("proxy", "", "Proxy URL (optional)")
		resume  = flag.Bool("resume", true, "Enable resume download (default: true)")
		timeout = flag.Int("timeout", 0, "Connection timeout in seconds (0 = no timeout)")
	)
	flag.Parse()

	if *url == "" || *output == "" {
		fmt.Fprintf(os.Stderr, "Usage: downloader -url <URL> -out <PATH> [-proxy <PROXY_URL>]\n")
		flag.PrintDefaults()
		os.Exit(1)
	}

	if *timeout > 0 {
		_ = time.Duration(*timeout) * time.Second
	}

	dl, err := downloader.NewDownloader(*proxy)
	if err != nil {
		fmt.Fprintf(os.Stderr, "Failed to create downloader: %v\n", err)
		os.Exit(1)
	}

	// 设置信号处理
	sigChan := make(chan os.Signal, 1)
	// 注意：Windows 下 signal.Notify 的行为可能不同
	go func() {
		<-sigChan
		fmt.Println(`{"type":"info","message":"Download cancelled by user"}`)
		dl.Cancel()
	}()
	_ = sigChan
	_ = syscall.SIGINT

	fmt.Printf("%s\n", mustJSON(downloader.ProgressMessage{Type: "info", Message: fmt.Sprintf("Starting download: %s", *url)}))
	fmt.Printf("%s\n", mustJSON(downloader.ProgressMessage{Type: "info", Message: fmt.Sprintf("Output: %s", *output)}))
	if *proxy != "" {
		fmt.Printf("%s\n", mustJSON(downloader.ProgressMessage{Type: "info", Message: fmt.Sprintf("Using proxy: %s", *proxy)}))
	}

	err = dl.Download(*url, *output, *resume, func(p downloader.ProgressMessage) {
		fmt.Println(mustJSON(p))
	})

	if err != nil {
		fmt.Printf("%s\n", mustJSON(downloader.ProgressMessage{Type: "error", Message: err.Error()}))
		os.Exit(1)
	}
}

func mustJSON(v interface{}) string {
	b, _ := json.Marshal(v)
	return string(b)
}
