package scanner

// Options 扫描选项
type Options struct {
	ExcludeDir      []string `json:"excludeDir,omitempty"`      // 排除的目录名
	TargetExtension []string `json:"targetExtension,omitempty"` // 目标扩展名
}

// ModInfo Mod 信息
type ModInfo struct {
	Name    string `json:"name"`
	Version string `json:"version"`
	Path    string `json:"path"`
}

// ModModel Mod 模型 (guid -> ModInfo)
type ModModel map[string]ModInfo

// PngParseResult PNG 解析结果
type PngParseResult struct {
	ModIDs      []string `json:"modIds"`
	CharaNames  []string `json:"charaNames"`
	GameDataLen int      `json:"gameDataLen"`
}

// PngModResult 单个 PNG 文件的 Mod 结果
type PngModResult struct {
	Path   string   `json:"path"`
	ModIDs []string `json:"modIds"`
}

// PngNamesResult 单个 PNG 文件的角色名结果
type PngNamesResult struct {
	Path  string   `json:"path"`
	Names []string `json:"names"`
}

// PngImageResult 单个 PNG 文件的缩略图结果
type PngImageResult struct {
	Path      string `json:"path"`
	ImageData string `json:"imageData"`
}

// PngPageDataResult 单个 PNG 文件的页面数据结果（名称+缩略图）
type PngPageDataResult struct {
	Path      string   `json:"path"`
	Names     []string `json:"names"`
	ImageData string   `json:"imageData"`
}
