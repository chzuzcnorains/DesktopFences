# 多显示器支持

## 1. 策略

```
1. 启动时枚举所有显示器 (Screen.AllScreens)
2. 每个 Fence 记录所属 MonitorIndex
3. Fence 拖动时不能跨越屏幕边界（吸附到屏幕边缘）
4. 显示器配置变化（插拔）→ 触发布局重新计算
   - Fences 5.5+ 的方案：按显示器配置保存独立布局
   - 配置 hash = Screen count + resolutions + DPI
   - 相同配置 → 恢复对应布局
   - 新配置 → 智能迁移（按比例缩放位置）
```

## 2. 配置哈希算法

```
输入: "{ScreenCount}|{Width}x{Height}@{X},{Y}:{P/S}|..."（按设备名排序）
输出: SHA256 前 16 字符（HEX）
```

## 3. DPI 处理

```
- 每个 FenceHost 窗口感知所在显示器的 DPI
- 使用 PerMonitorDpiAware 模式
- 窗口跨 DPI 边界时触发 WM_DPICHANGED → 重新布局
```

## 4. 热插拔处理流程

```
DisplaySettingsChanged → 计算新 ConfigHash → 与旧值比较
  → 匹配已有布局: 恢复该布局
  → 新配置: ClampToMonitor 限制 Fence 到新屏幕范围
  → 保存旧配置布局以备后用
```

## 5. 已实现组件

- `MonitorManager`（Shell）— 显示器枚举、SHA256 配置哈希（16 字符）、热插拔检测
- `SystemEvents.DisplaySettingsChanged` — 监听显示器配置变化
- 按 ConfigHash 独立保存/恢复布局（`monitor-layouts/{hash}.json`）
- `ClampToMonitor()` — Fence 范围限制到所属显示器工作区（加载时、显示器变化时均执行）
- `SpawnFenceWindow()` 启动时自动调用 `ClampToMonitor` 校正坐标，防止 Fence 窗口落在屏幕外不可见
- `MigrateLayout()` — 配置变化时按比例缩放迁移 Fence 位置
