package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"strings"

	"wails/internal/scanner"
)

// Request 命令请求结构
type Request struct {
	Action      string          `json:"action"`
	Path        string          `json:"path"`
	Paths       []string        `json:"paths,omitempty"`
	TargetPath  string          `json:"targetPath"`
	Options     *scanner.Options `json:"options,omitempty"`
	Concurrency int             `json:"concurrency,omitempty"`
}

// Response 响应结构
type Response struct {
	Success bool        `json:"success"`
	Data    interface{} `json:"data,omitempty"`
	Error   string      `json:"error,omitempty"`
}

func sendSuccess(data interface{}) {
	resp := Response{Success: true, Data: data}
	json.NewEncoder(os.Stdout).Encode(resp)
}

func sendError(msg string) {
	resp := Response{Success: false, Error: msg}
	json.NewEncoder(os.Stdout).Encode(resp)
}

func main() {
	var (
		action      = flag.String("action", "", "Action")
		path        = flag.String("path", "", "Input file or directory path")
		targetPath  = flag.String("target", "", "Target path (for moveFile)")
		excludeDirs = flag.String("exclude", "", "Comma-separated directory names to exclude")
		extensions  = flag.String("ext", "", "Comma-separated target extensions")
		jsonInput   = flag.Bool("json", false, "Read request from stdin as JSON")
	)
	flag.Parse()

	var req Request

	if *jsonInput {
		decoder := json.NewDecoder(os.Stdin)
		if err := decoder.Decode(&req); err != nil {
			sendError(fmt.Sprintf("Failed to parse JSON input: %v", err))
			os.Exit(1)
		}
	} else {
		req.Action = *action
		req.Path = *path
		req.TargetPath = *targetPath

		if *excludeDirs != "" {
			req.Options = &scanner.Options{
				ExcludeDir: strings.Split(*excludeDirs, ","),
			}
		}
		if *extensions != "" {
			if req.Options == nil {
				req.Options = &scanner.Options{}
			}
			exts := strings.Split(*extensions, ",")
			for i, ext := range exts {
				if !strings.HasPrefix(ext, ".") {
					exts[i] = "." + ext
				}
			}
			req.Options.TargetExtension = exts
		}
	}

	var result interface{}
	var err error

	switch req.Action {
	case "scanDir":
		result, err = scanner.ScanDirectory(req.Path, req.Options)
	case "readPngMods":
		result, err = scanner.ReadPngMods(req.Path)
	case "readPngNames":
		result, err = scanner.ReadPngNames(req.Path)
	case "readPngImage":
		result, err = scanner.ReadPngImage(req.Path)
	case "readZipMod":
		result, err = scanner.ReadZipMod(req.Path)
	case "moveFile":
		err = scanner.MoveFile(req.Path, req.TargetPath)
	case "checkDir":
		err = scanner.CheckTargetDir(req.Path)
	case "fileExists":
		result, err = scanner.FileExists(req.Path)
	case "readZipModBatch":
		result, err = scanner.ReadZipModBatch(req.Paths, req.Concurrency)
	case "readPngModsBatch":
		result, err = scanner.ReadPngModsBatch(req.Paths, req.Concurrency)
	case "readPngNamesBatch":
		result, err = scanner.ReadPngNamesBatch(req.Paths, req.Concurrency)
	case "readPngImagesBatch":
		result, err = scanner.ReadPngImagesBatch(req.Paths, req.Concurrency)
	case "readPngPageDataBatch":
		result, err = scanner.ReadPngPageDataBatch(req.Paths, req.Concurrency)
	default:
		sendError(fmt.Sprintf("Unknown action: %s", req.Action))
		os.Exit(1)
	}

	if err != nil {
		sendError(err.Error())
		os.Exit(1)
	}

	sendSuccess(result)
}
