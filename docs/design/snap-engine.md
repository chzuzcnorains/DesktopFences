# Snap 吸附系统

## 1. 吸附目标

```
吸附目标：
  ├─ 屏幕边缘（上下左右）
  ├─ 其他 Fence 的边缘（上下左右对齐）
  └─ 网格线（可配置间距，如 8px 或 16px）
```

## 2. 吸附算法

```
SnapEngine.cs — 纯函数，无副作用
  输入：moving Rect + other Rects + screen bounds
  输出：吸附修正后的 SnapResult

算法步骤：
  1. 拖动时计算当前 Fence 四条边的位置
  2. 遍历所有吸附目标，找到距离 < threshold (默认 10px) 的边
  3. 将位置修正到吸附点
  4. 同时吸附多条边（如左边吸附屏幕边缘 + 上边吸附另一个 Fence 底部）
  5. 按住 Alt 拖动时临时禁用吸附
```

## 3. 配置

- `SnapThreshold` — 吸附距离阈值，默认 10px
- `CompatibilityMode` — 禁用 z-order 管理（兼容其他桌面增强工具）
