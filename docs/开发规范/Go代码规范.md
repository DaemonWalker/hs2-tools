# Go 代码规范

本文档定义 HS2-Tools 项目中 Go 代码的编写规范，强调**函数式编程风格**。

## 核心原则

### 1. 函数式优先

Go 不是面向对象语言，不要用结构体方法模拟类。

```go
// ✅ 纯函数 - 相同输入永远返回相同输出，无副作用
func ProcessData(input Data, opts Options) (Result, error) {
    // 处理逻辑
    return result, nil
}

// ✅ 函数组合
pipeline := compose(parse, validate, transform)
result := pipeline(input)

// ❌ 避免类风格（不要用结构体模拟类）
type Processor struct {
    config Config
}
func (p *Processor) Process(data Data) Result { ... }
```

### 2. 数据与逻辑分离

不要封装在结构体中，让数据流动。

```go
// ✅ 数据作为参数传递
func CalculateTotal(items []Item, taxRate float64) float64 {
    var total float64
    for _, item := range items {
        total += item.Price * float64(item.Quantity)
    }
    return total * (1 + taxRate)
}

// ❌ 避免将数据封装在"类"中
type Calculator struct {
    items []Item
    taxRate float64
}
func (c *Calculator) Calculate() float64 { ... }
```

### 3. 不可变数据

不修改输入，返回新值。

```go
// ✅ 返回新值，不修改原值
func FilterItems(items []Item, predicate func(Item) bool) []Item {
    result := make([]Item, 0, len(items))
    for _, item := range items {
        if predicate(item) {
            result = append(result, item)
        }
    }
    return result
}

// ❌ 避免修改输入参数
func BadFilter(items *[]Item) {
    // 直接修改切片
}
```

### 4. 避免接口过度设计

不需要预先定义接口，让接口自然浮现。

```go
// ✅ 先写具体实现
func ProcessJSON(data []byte) (Result, error) {
    // 实现
}

// ✅ 真的有需要时再定义接口
type Parser func([]byte) (Result, error)

// ❌ 不要过早抽象
interface DataProcessor {
    Process(data []byte) (Result, error)
}
```

### 5. 错误作为值

使用 `result, err` 模式，不抛异常。

```go
// ✅ 错误作为返回值
func ReadConfig(path string) (Config, error) {
    data, err := os.ReadFile(path)
    if err != nil {
        return Config{}, fmt.Errorf("read config: %w", err)
    }

    var cfg Config
    if err := json.Unmarshal(data, &cfg); err != nil {
        return Config{}, fmt.Errorf("parse config: %w", err)
    }

    return cfg, nil
}

// ✅ 调用方处理错误
cfg, err := ReadConfig("config.json")
if err != nil {
    log.Printf("Failed to load config: %v", err)
    // 处理错误
}
```

## 命名规范

### 文件命名

| 类型 | 命名规范 | 示例 |
|------|----------|------|
| 主文件 | snake_case.go | `main.go`, `sideloader.go` |
| 工具模块 | snake_case.go | `zip_reader.go`, `file_utils.go` |
| 测试文件 | snake_case_test.go | `parser_test.go` |

### 函数命名

```go
// ✅ 导出的函数 - PascalCase，描述功能
func DownloadFile(url string, dest string) error
func ParseMetadata(data []byte) (Metadata, error)
func ValidatePath(path string) bool

// ✅ 内部函数 - camelCase
func calculateChecksum(data []byte) string
func normalizePath(path string) string

// ✅ 布尔判断 - is/has/should 前缀
func IsValidExtension(ext string) bool
func HasRequiredFiles(dir string) bool
func ShouldUpdate(local, remote time.Time) bool
```

### 变量命名

```go
// ✅ 短命名（局部变量）
for i, v := range items { }
if err != nil { }

// ✅ 描述性命名（函数参数、返回值）
func ProcessFile(filePath string, bufferSize int) (processedBytes int64, err error)

// ✅ 常量 - 描述性命名
const MaxBufferSize = 1024 * 1024
const DefaultTimeout = 30 * time.Second
```

## 代码结构

### 包组织

```go
// ✅ 按功能组织包
wails/
├── internal/
│   ├── sideloader/     # 爬虫工具
│   │   ├── sideloader.go
│   │   └── zipreader.go
│   ├── downloader/     # 下载工具
│   │   └── downloader.go
│   ├── scanner/        # 扫描工具
│   │   ├── scanner.go
│   │   └── png.go
│   └── utils/          # 配置持久化等工具
│       └── config.go
└── cmd/                # 独立命令行入口
    ├── sideloader/
    ├── downloader/
    └── scanner/
```

### 函数组织

```go
// 文件：processor.go

// 1. 包声明和导入
package processor

import (
    "fmt"
    "os"
)

// 2. 常量定义
const (
    DefaultWorkers = 4
    MaxQueueSize   = 100
)

// 3. 类型定义（简单结构体，无方法）
type Task struct {
    ID      string
    Payload []byte
    Retry   int
}

type Result struct {
    TaskID  string
    Success bool
    Error   string
}

// 4. 导出函数（按功能分组）

// ProcessTasks 处理任务队列
func ProcessTasks(tasks []Task, workers int) []Result {
    // 实现
}

// ValidateTask 验证任务有效性
func ValidateTask(task Task) error {
    // 实现
}

// 5. 内部辅助函数

func splitBatches(tasks []Task, batchSize int) [][]Task {
    // 实现
}
```

## 函数设计

### 参数设计

```go
// ✅ 使用 Options 模式处理可选参数
func NewDownloader(opts DownloadOptions) (*Downloader, error)

type DownloadOptions struct {
    Timeout       time.Duration
    RetryCount    int
    WorkerCount   int
    ProgressHook  func(int64, int64)
}

// ✅ 函数组合
func WithRetry(fn func() error, maxRetries int) error
func WithTimeout(fn func() error, timeout time.Duration) error
```

### 返回值设计

```go
// ✅ 返回值 + error（Go 惯用法）
func FindItem(items []Item, id string) (Item, bool)
func ParseInt(s string) (int, error)

// ✅ 多返回值命名（提高可读性）
func Divide(dividend, divisor float64) (quotient float64, remainder float64, err error)
```

## 错误处理

### 错误包装

```go
// ✅ 使用 fmt.Errorf 包装错误
func LoadConfig(path string) (Config, error) {
    data, err := os.ReadFile(path)
    if err != nil {
        return Config{}, fmt.Errorf("read config file %s: %w", path, err)
    }
    // ...
}

// ✅ 定义 Sentinel 错误
var (
    ErrNotFound     = errors.New("resource not found")
    ErrInvalidInput = errors.New("invalid input")
)

// ✅ 使用 errors.Is 判断错误类型
if errors.Is(err, ErrNotFound) {
    // 处理未找到
}
```

### 错误返回时机

```go
// ✅ 尽早返回，减少嵌套
func Process(input string) (Result, error) {
    if input == "" {
        return Result{}, ErrInvalidInput
    }

    data, err := fetchData(input)
    if err != nil {
        return Result{}, fmt.Errorf("fetch data: %w", err)
    }

    return transform(data), nil
}
```

## 并发模式

### Channel 使用

```go
// ✅ 使用 channel 传递数据
func WorkerPool(jobs <-chan Job, results chan<- Result, workerCount int) {
    var wg sync.WaitGroup
    for i := 0; i < workerCount; i++ {
        wg.Add(1)
        go func() {
            defer wg.Done()
            for job := range jobs {
                results <- process(job)
            }
        }()
    }
    wg.Wait()
}

// ✅ 使用 context 控制生命周期
func ProcessWithContext(ctx context.Context, tasks []Task) error {
    ctx, cancel := context.WithTimeout(ctx, 30*time.Second)
    defer cancel()

    for _, task := range tasks {
        select {
        case <-ctx.Done():
            return ctx.Err()
        default:
            if err := process(task); err != nil {
                return err
            }
        }
    }
    return nil
}
```

## 测试规范

### 测试函数

```go
// ✅ 测试函数命名
func TestProcessData(t *testing.T)
func TestProcessData_EmptyInput(t *testing.T)  // 边界情况
func BenchmarkProcessData(b *testing.B)

// ✅ 表驱动测试
func TestValidatePath(t *testing.T) {
    tests := []struct {
        name    string
        path    string
        wantErr bool
    }{
        {"valid absolute", "/home/user/file", false},
        {"valid relative", "./file", false},
        {"empty", "", true},
        {"with null", "/path\x00/file", true},
    }

    for _, tt := range tests {
        t.Run(tt.name, func(t *testing.T) {
            err := ValidatePath(tt.path)
            if (err != nil) != tt.wantErr {
                t.Errorf("ValidatePath() error = %v, wantErr %v", err, tt.wantErr)
            }
        })
    }
}
```

## 代码风格

### 格式化

- 使用 `gofmt` 自动格式化
- 使用 `goimports` 管理导入
- 行长度建议不超过 100 字符

### 注释

```go
// ✅ 包注释
// Package sideloader 提供模组安装功能
package sideloader

// ✅ 函数注释（导出的函数）
// InstallMod 安装指定路径的模组文件
// 返回安装后的路径和可能发生的错误
func InstallMod(sourcePath, targetDir string) (string, error)

// ✅ 类型注释
type Config struct {
    // SourceDir 源文件目录
    SourceDir string

    // TargetDir 目标安装目录
    TargetDir string

    // Workers 并发工作数，默认 4
    Workers int
}
```

## 反模式清单

```go
// ❌ 避免 getter/setter
type item struct { name string }
func (i *item) GetName() string { return i.name }  // 不需要
func (i *item) SetName(n string) { i.name = n }    // 不需要

// ✅ 直接导出字段或返回副本
type Item struct { Name string }

// ❌ 避免空接口泛滥
func Process(data interface{}) interface{}  // 类型丢失

// ✅ 使用具体类型或泛型（Go 1.18+）
func Process[T Input](data T) Output

// ❌ 避免 panic 传播
func MustLoadConfig(path string) Config {
    cfg, err := LoadConfig(path)
    if err != nil {
        panic(err)  // 只在 init() 中使用
    }
    return cfg
}

// ✅ 返回 error 让调用方决定
func LoadConfig(path string) (Config, error)
```
