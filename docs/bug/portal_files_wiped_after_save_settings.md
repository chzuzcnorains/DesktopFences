# 保存设置后 Portal Fence 内容被清空

## 问题描述

打开 Portal Fence(映射文件夹模式)展示外部文件夹内容,然后打开 设置 → 任意面板 → 保存。Portal Fence 立刻变空,标题栏的 📁 前缀仍在,空状态提示是「右键标题栏可更改映射文件夹」(说明 `IsPortalMode == true`,只是 `Files` 集合被清空)。

复现路径任意一个改动都触发(切 IconStyle、改 AccentColor、改 IconSize、调 Hue/Opacity 等),只要点了「保存」按钮即可。

## 问题根源

两个独立设计相加产生的 bug:

### 来源 1:SettingsWindow 保存按钮无条件触发两个事件

[SettingsWindow.xaml.cs:156-164](../../src/DesktopFences.UI/Controls/SettingsWindow.xaml.cs#L156-L164) 的 `BtnSave_Click` 在保存 AppSettings 时也无条件触发 `RulesSaved`,即使用户根本没改规则:

```csharp
SettingsSaved?.Invoke(_settings);
RulesSaved?.Invoke(PaneRules.GetRules());
```

设计上 RulesSaved 表示「规则已变更」契约,无差别触发会让下游误以为规则改了。

### 来源 2:ReEvaluateClassifiedFiles 不区分 portal fence

[App.xaml.cs:438](../../src/DesktopFences.App/App.xaml.cs#L438) 把 `RulesSaved` 绑到 `ReEvaluateClassifiedFiles()`,该方法遍历**所有 tab**(含 portal),按规则筛掉「不被任何规则匹配到本 fence」的文件:

```csharp
foreach (var tab in _fenceWindows.AllTabs())
{
    var filesToRemove = tab.Files
        .Where(f =>
        {
            var matched = _ruleEngine?.Match(f.FilePath, _rules);
            return matched is null || matched.TargetFenceId != tab.Id;
        })
        .Select(f => f.FilePath)
        .ToList();
    foreach (var path in filesToRemove)
        tab.RemoveFile(path);
}
```

Portal Fence 的内容是被映射文件夹里的所有文件(如 `D:\Documents\foo.docx`),**这些路径没有任何规则把它们指向当前 portal fence**——所以一律 `RemoveFile`,fence 立刻变空。

`FolderPortalWatcher` 的语义是 **增量同步**(只在文件系统真发生变化时增删),它不会主动重新灌入已经被清空的文件——所以用户除非手动重设 portal 路径或在 OS 上动文件,否则 fence 永远空着。

## 修复方案

最小改动:在 `ReEvaluateClassifiedFiles` 循环开头跳过 portal mode tab。

```csharp
foreach (var tab in _fenceWindows.AllTabs())
{
    // Portal fence 内容来自被映射的外部文件夹,不参与规则分类。
    // 规则引擎不会把外部文件路径匹配到 portal fence,跳过避免被清空。
    if (tab.IsPortalMode) continue;

    // ...原逻辑不变
}
```

### 为什么不修 `BtnSave_Click` 的双事件触发?

替代方案是让 SettingsWindow 比对 `PaneRules.GetRules()` 与原 rules 集合,只在真变化时触发 `RulesSaved`。但这个改动需要在 UI 层维护规则集合的"原始快照",而且违反了「保存就重评估」的现有契约——其他订阅方(虽然现在只有一个)可能依赖这个无条件触发。在 `ReEvaluateClassifiedFiles` 一处加 `if (tab.IsPortalMode) continue;` 影响面最小,语义最清晰:**规则分类不应触及映射文件夹的内容**。

## 修复效果

- ✅ 保存任意设置后,portal fence 内容保留
- ✅ 普通规则分类 fence 仍按规则重新评估(行为不变)
- ✅ Portal 与规则可以共存:同一应用既可有 portal fence 又可有规则分类 fence,互不干扰

## 经验总结

1. **Portal 与规则分类是两套独立的内容来源**——portal 由 watcher 增量同步,规则分类由文件系统监控 + RuleEngine 路由。两套机制汇聚到同一个 `Files` 集合时,任何全局清理操作都必须显式区分来源。否则 cleanup 会把另一套来源的内容当作"孤儿"误删。
2. **"保存"按钮触发的事件越多,组件耦合越深**——SettingsWindow 的 `BtnSave_Click` 同时触发 SettingsSaved + RulesSaved 是一个隐性的"all-fire"设计,任何监听者只看自己感兴趣的事件,但实际上每次保存都会触发所有事件。在加新订阅者时要意识到这个"无条件触发"语义。
3. **增量同步 ≠ 完整重灌**——`FolderPortalWatcher` 只对文件系统的 delta 反应,**不会**因为 fence 变空就主动重灌。如果有代码可能清空 portal Files,要么自己负责重灌,要么更早地阻止它清空(本次修复采取后者)。

## 相关文件

- 修复点:[src/DesktopFences.App/App.xaml.cs:193-217](../../src/DesktopFences.App/App.xaml.cs#L193-L217)(`ReEvaluateClassifiedFiles`)
- 触发点:[src/DesktopFences.UI/Controls/SettingsWindow.xaml.cs:161-162](../../src/DesktopFences.UI/Controls/SettingsWindow.xaml.cs#L161-L162)(`BtnSave_Click` 同时 fire 两事件)
- 订阅点:[src/DesktopFences.App/App.xaml.cs:438](../../src/DesktopFences.App/App.xaml.cs#L438)(RulesSaved → ReEvaluateClassifiedFiles)
- 设计参考:[docs/design/folder-portal.md](../design/folder-portal.md)
