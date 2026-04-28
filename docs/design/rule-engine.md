# 规则引擎设计

## 1. 规则类型与优先级

```
规则优先级（数字越小越优先）：
  Priority 1: 用户手动放入的文件 → 不受规则影响
  Priority 10: 扩展名规则 → .exe/.lnk → "应用程序" Fence
  Priority 20: 名称 Glob 规则 → report*.docx → "报告" Fence
  Priority 30: 日期范围规则 → 本周创建 → "最近文件" Fence
  Priority 40: 大小范围规则 → >100MB → "大文件" Fence
```

## 2. 条件匹配实现

| 规则类型 | 实现方式 |
|----------|----------|
| Extension | string.Split(',') + 逐个比较 Path.GetExtension() |
| NameGlob | 转换为 Regex（* → .*, ? → .）+ Regex.IsMatch() |
| DateRange | File.GetCreationTime() / GetLastWriteTime() 范围比较 |
| SizeRange | FileInfo.Length 范围比较 |
| Regex | 直接 Regex.IsMatch(fileName) |
| IsDirectory | Directory.Exists(filePath) 判断路径是否为文件夹 |

**无效正则的处理**：返回 false 不抛异常

## 3. 规则评估流程

```
1. FileMonitor 检测到桌面新增文件，或 AutoOrganizeTimer 触发整理
2. 按 Priority 排序遍历所有 enabled 规则
3. 第一个匹配的规则决定目标 Fence
4. 如果无规则匹配 → 文件留在桌面（不移入任何 Fence）
5. 如果有匹配的规则但目标 Fence 不存在 → 自动创建同名 Fence
6. 规则冲突时：最高优先级（数字最小）胜出
```

## 4. 自动创建 Fence 功能

当启用的规则匹配到文件但目标 Fence 不存在时，系统会自动创建 Fence：

- **创建时机**：`OnDesktopFilesAdded` 和 `OrganizeDesktopOnceAsync` 中检测到目标 Fence 缺失时
- **Fence 名称**：使用规则的名称作为新 Fence 的标题
- **Fence ID**：使用规则的 `TargetFenceId` 作为新 Fence 的 ID，确保后续规则能正确匹配
- **位置**：在最后一个 Fence 附近偏移（+50, +50），若无 Fence 则在 (200, 200)
- **规则关联**：新 Fence 的 `RuleIds` 列表会包含创建它的规则 ID

## 5. 规则持久化

- `ILayoutStore` — 新增 `LoadRulesAsync()` / `SaveRulesAsync()` 接口
- `JsonLayoutStore` — 新增 `rules.json` 文件读写，原子写入
- 规则编辑器已合并至 `SettingsWindow` → `RulesSettingsPane`

## 6. 默认分类配置（首次运行）

首次启动（`fences.json` 不存在）时，`CreateDefaultConfiguration()` 自动创建 6 个 Fence + 6 条规则：

| Fence 名称 | 位置 | 规则类型 | 匹配内容 |
|-----------|------|---------|---------|
| 程序及快捷方式 | (20, 20) | Extension | .exe,.lnk,.url,.bat,.cmd,.ps1,.msi |
| 文件夹 | (340, 20) | IsDirectory | — |
| 文档 | (20, 240) | Extension | .doc,.docx,.pdf,.txt,.xls,.xlsx,.ppt,.pptx,.md,.rtf,.csv 等 |
| 视频 | (340, 240) | Extension | .mp4,.mkv,.avi,.mov,.wmv,.flv,.webm,.ts,.rmvb 等 |
| 音乐 | (20, 460) | Extension | .mp3,.wav,.flac,.aac,.ogg,.m4a,.wma,.opus,.ape 等 |
| 图片 | (340, 460) | Extension | .jpg,.jpeg,.png,.gif,.bmp,.webp,.svg,.ico,.tiff,.heic,.raw 等 |

规则 Priority 1-6，Fence 与规则通过 `Id`（Guid）关联。

## 7. 规则变更时重新分类

`ReEvaluateClassifiedFiles` — 规则保存后触发：
- 遍历所有 Fence 中的文件重新匹配
- 不匹配的文件从 Fence 移除，若仍在桌面则添加回覆盖层
