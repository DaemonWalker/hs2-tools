package scanner

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

// ScanDirectory 递归扫描目录
func ScanDirectory(dir string, opts *Options) ([]string, error) {
	if opts == nil {
		opts = &Options{}
	}

	var files []string

	err := filepath.Walk(dir, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return nil // 跳过无法访问的文件
		}

		// 检查是否需要排除目录
		if info.IsDir() {
			for _, exclude := range opts.ExcludeDir {
				if strings.Contains(info.Name(), exclude) {
					return filepath.SkipDir
				}
			}
			return nil
		}

		// 检查扩展名
		if len(opts.TargetExtension) > 0 {
			ext := filepath.Ext(path)
			found := false
			for _, targetExt := range opts.TargetExtension {
				if strings.EqualFold(ext, targetExt) {
					found = true
					break
				}
			}
			if !found {
				return nil
			}
		}

		files = append(files, path)
		return nil
	})

	return files, err
}

// MoveFile 移动文件（支持跨盘符）
func MoveFile(src, dst string) error {
	// 确保目标目录存在
	dir := filepath.Dir(dst)
	if err := os.MkdirAll(dir, 0755); err != nil {
		return fmt.Errorf("failed to create target directory: %w", err)
	}

	// 尝试直接重命名（同盘符）
	err := os.Rename(src, dst)
	if err != nil {
		// 跨盘符移动：复制后删除
		if copyErr := copyFile(src, dst); copyErr != nil {
			return fmt.Errorf("failed to copy file: %w", copyErr)
		}
		if removeErr := os.Remove(src); removeErr != nil {
			return fmt.Errorf("failed to remove source file: %w", removeErr)
		}
	}
	return nil
}

// copyFile 复制文件（用于跨盘符移动）
func copyFile(src, dst string) error {
	input, err := os.ReadFile(src)
	if err != nil {
		return err
	}
	return os.WriteFile(dst, input, 0644)
}

// CheckTargetDir 检查并创建目标目录
func CheckTargetDir(target string) error {
	if _, err := os.Stat(target); os.IsNotExist(err) {
		return os.MkdirAll(target, 0755)
	}
	return nil
}

// FileExists 检查文件是否存在且是 PNG
func FileExists(path string) (bool, error) {
	info, err := os.Stat(path)
	if err != nil {
		return false, nil
	}
	if info.IsDir() {
		return false, nil
	}
	return strings.EqualFold(filepath.Ext(path), ".png"), nil
}
