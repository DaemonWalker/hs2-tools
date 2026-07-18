# HS2-Tools 设计系统

## 颜色系统

### 主题色
- `--primary-color` - 主色调（赛博紫夜: #8b5cf6, 简洁专业: #1890ff）
- `--theme-primary` - 主色调别名
- `--theme-primary-light` - 浅色变体
- `--theme-primary-dark` - 深色变体

### 状态色（跨主题一致）
- `--color-success` / `#52c41a` - 成功状态
- `--color-error` / `#ff4d4f` - 错误状态
- `--color-warning` / `#faad14` - 警告状态
- `--color-info` / `#1677ff` - 信息状态

### 状态背景色
- `--color-success-bg` / `#f6ffed` - 成功背景
- `--color-error-bg` / `#fff2f0` - 错误背景
- `--color-warning-bg` / `#fffbe6` - 警告背景
- `--color-info-bg` / `#e6f7ff` - 信息背景

### 中性色
- `--color-gray-100` ~ `--color-gray-700` - 灰阶

### 主题背景色
- `--bg-primary` - 主背景
- `--bg-secondary` - 次背景（卡片）
- `--bg-tertiary` - 第三背景

### 主题文字色
- `--text-primary` - 主文字
- `--text-secondary` - 次文字/描述
- `--text-muted` - 弱化文字

### 边框
- `--border-color` - 主边框色

## 间距系统

| Token | 值 | 用途 |
|-------|-----|------|
| `--spacing-xs` | 4px | 极小间距 |
| `--spacing-sm` | 8px | 小间距 |
| `--spacing-md` | 16px | 标准间距 |
| `--spacing-lg` | 24px | 大间距 |
| `--spacing-xl` | 32px | 极大间距 |

## 圆角系统

| Token | 值 | 用途 |
|-------|-----|------|
| `--radius-sm` | 8px | 小圆角（按钮、标签）|
| `--radius-md` | 12px | 标准圆角（卡片）|
| `--radius-lg` | 16px | 大圆角（面板）|
| `--radius-xl` | 24px | 极大圆角（特殊）|

## 使用规范

### 1. 颜色使用
```tsx
// ✅ 正确 - 使用 CSS 变量
<HeartFilled style={{ color: 'var(--color-error)' }} />
<CheckCircleOutlined style={{ color: 'var(--color-success)' }} />
<Button style={{ background: 'var(--theme-primary)' }} />

// ❌ 错误 - 硬编码颜色
<HeartFilled style={{ color: '#ff4d4f' }} />
<CheckCircleOutlined style={{ color: '#52c41a' }} />
```

### 2. 间距使用
```tsx
// ✅ 正确 - 使用设计系统间距
<div style={{ padding: 'var(--spacing-md)' }} />
<div style={{ gap: 'var(--spacing-sm)' }} />

// 或在 CSS 中
.my-component {
  padding: var(--spacing-md);
  gap: var(--spacing-sm);
}
```

### 3. 圆角使用
```tsx
// ✅ 正确
<Card style={{ borderRadius: 'var(--radius-md)' }} />
<Button style={{ borderRadius: 'var(--radius-sm)' }} />
```

## 组件模式

### 卡片
- 背景: `var(--bg-secondary)`
- 圆角: `var(--radius-md)` (12px)
- 内边距: `var(--spacing-md)` (16px)
- 阴影: 根据主题使用相应阴影

### 按钮
- 主按钮: `var(--theme-primary)` 背景
- 圆角: `var(--radius-sm)` (8px)
- 内边距: 标准 Ant Design 按钮

### 图标
- 主要操作: `var(--theme-primary)`
- 成功状态: `var(--color-success)`
- 错误状态: `var(--color-error)`
- 警告状态: `var(--color-warning)`
- 弱化/禁用: `var(--text-secondary)`
