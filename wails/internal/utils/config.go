package utils

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
)

// getConfigDir 获取配置文件目录
func getConfigDir() (string, error) {
	configDir, err := os.UserConfigDir()
	if err != nil {
		return "", err
	}
	dir := filepath.Join(configDir, "hs2-tools")
	if err := os.MkdirAll(dir, 0755); err != nil {
		return "", err
	}
	return dir, nil
}

// SaveJSON 将数据保存为 JSON 文件
func SaveJSON(filename string, data interface{}) error {
	dir, err := getConfigDir()
	if err != nil {
		return err
	}
	path := filepath.Join(dir, filename)
	bytes, err := json.MarshalIndent(data, "", "  ")
	if err != nil {
		return fmt.Errorf("failed to marshal JSON: %w", err)
	}
	return os.WriteFile(path, bytes, 0644)
}

// LoadJSON 从 JSON 文件加载数据
func LoadJSON(filename string) (map[string]interface{}, error) {
	dir, err := getConfigDir()
	if err != nil {
		return nil, err
	}
	path := filepath.Join(dir, filename)
	data, err := os.ReadFile(path)
	if err != nil {
		if os.IsNotExist(err) {
			return nil, nil
		}
		return nil, fmt.Errorf("failed to read file: %w", err)
	}
	var result map[string]interface{}
	if err := json.Unmarshal(data, &result); err != nil {
		return nil, fmt.Errorf("failed to unmarshal JSON: %w", err)
	}
	return result, nil
}

// SaveSettings 保存设置
func SaveSettings(settings interface{}) error {
	return SaveJSON("setting.json", settings)
}

// LoadSettings 加载设置
func LoadSettings() (map[string]interface{}, error) {
	return LoadJSON("setting.json")
}

// SaveLocalMods 保存本地 Mod 数据
func SaveLocalMods(mods interface{}) error {
	return SaveJSON("localMods.json", mods)
}

// LoadLocalMods 加载本地 Mod 数据
func LoadLocalMods() (map[string]interface{}, error) {
	return LoadJSON("localMods.json")
}
