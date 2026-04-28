# 待完成功能（分散在各 Phase 中）

### Phase 6 待完成
- [x] Snap 吸附磁性效果（视觉反馈）✅ WM_MOVING 实时吸附 + SnapGuideOverlay 辅助线 + Alt 禁用 + Resize 吸附
- [x] Inno Setup 安装包 ✅ `tools/installer/DesktopFences.iss` + `build-installer.ps1`
- [ ] 自动更新检查（需 GitHub 仓库地址）

### Phase 8 待完成
- [ ] DWM 背景模糊

## 已移除项（功能已被替代或无需实现）

- ~~Chameleon 模式~~ → `FenceBgHue`(0-360) + `FenceOpacity` + `AccentColor` 已覆盖色调定制，手动调色比自动采样更灵活
- ~~Icon Tint~~ → `UseCustomFileIcons` + `AccentColor` + `FileKindToIconConverter` 已覆盖图标着色需求
- ~~快捷方式目标解析 IShellLink~~ → .lnk 已有 Shell 图标提取和规则分类，解析目标仅对"按目标类型分类快捷方式"有用，场景极小众
- ~~用户手动放入的文件标记为"手动"~~ → `ReEvaluateClassifiedFiles` 仅在规则大幅变更时触发，重新拖入很方便，追踪来源的复杂度远大于收益
