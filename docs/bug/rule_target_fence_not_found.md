# 启用规则时自动创建缺失的Fence功能bug修复

## 问题描述
当启用某个规则被禁用后重新启用，或者规则的目标Fence被删除后，启用该规则时，系统没有自动创建对应的Fence。

**测试场景：
1. 用户有一个"音乐"规则，指向"音乐" Fence
2. 用户禁用"音乐"规则，保存
3. 用户关闭或删除"音乐" Fence
4. 用户重新启用"音乐"规则，保存
5. 期望：自动创建"音乐" Fence
6. 实际：没有创建，或者规则被错误地绑定到其他Fence

## 产生原因
### 1. 规则设置界面自动选择第一个Fence
在 `RulesSettingsPane.xaml.cs` 的 `PopulateRuleForm` 方法中，当找不到规则原来的目标Fence时，代码会自动选择下拉列表中的第一个Fence：

```csharp
if (CboTargetFence.SelectedIndex < 0 && CboTargetFence.Items.Count > 0)
    CboTargetFence.SelectedIndex = 0;
```

这导致规则的 `TargetFenceId` 被错误地修改为指向第一个Fence（如"SQL"），而不是保持原样以便后续自动创建。

### 2. 缺少自动创建Fence的逻辑
虽然之前只有在文件需要整理（`OrganizeDesktopOnceAsync`）或有新文件（`OnDesktopFilesAdded`）时才会创建Fence，但用户期望在启用规则时就自动创建。

## 修复方案

### 1. 修改规则设置界面逻辑
在 `RulesSettingsPane.xaml.cs` 中移除自动选择第一个Fence的逻辑：

```csharp
// 找不到目标 Fence 时，不要自动选择第一个，保持 TargetFenceId 不变
// 这样保存时会自动创建同名的 Fence
```

### 2. 在保存规则时自动创建缺失的Fence
在 `App.xaml.cs` 的 `RulesSaved` 事件处理中，遍历所有启用的规则，如果目标Fence不存在，则自动创建一个同名的Fence：

```csharp
settingsWindow.RulesSaved += newRules =>
{
    _rules = newRules;
    _ = SaveRulesAsync();

    // 为启用的规则创建缺失的 Fence
    foreach (var rule in _rules.Where(r => r.IsEnabled))
    {
        var existingFence = FindFenceById(rule.TargetFenceId);
        if (existingFence == null)
        {
            CreateFenceForRule(rule);
        }
    }

    ReEvaluateClassifiedFiles();
};
```

### 3. 添加辅助方法
添加 `FindFenceById` 方法简化查找逻辑：

```csharp
private FencePanelViewModel? FindFenceById(Guid fenceId)
{
    if (fenceId == Guid.Empty)
        return null;

    foreach (var host in _fenceWindows)
    {
        foreach (var tab in host.Tabs)
        {
            if (tab.Id == fenceId)
                return tab;
        }
    }
    return null;
}
```

### 4. 修复下拉框显示问题
在 `RulesSettingsPane.xaml` 中为 `CboTargetFence` 添加 `DisplayMemberPath="Title"`，确保显示Fence标题而不是类型名。

## 核心代码修改

### RulesSettingsPane.xaml.cs
```csharp
private void PopulateRuleForm(ClassificationRule rule)
{
    // ... 其他代码 ...

    CboTargetFence.SelectedItem = null;
    foreach (FenceDefinition fence in CboTargetFence.Items)
    {
        if (fence.Id == rule.TargetFenceId)
        {
            CboTargetFence.SelectedItem = fence;
            break;
        }
    }
    // 找不到目标 Fence 时，不要自动选择第一个，保持 TargetFenceId 不变
    // 这样保存时会自动创建同名的 Fence
}
```

### App.xaml.cs
```csharp
settingsWindow.RulesSaved += newRules =>
{
    _rules = newRules;
    _ = SaveRulesAsync();

    // 为启用的规则创建缺失的 Fence
    foreach (var rule in _rules.Where(r => r.IsEnabled))
    {
        var existingFence = FindFenceById(rule.TargetFenceId);
        if (existingFence == null)
        {
            CreateFenceForRule(rule);
        }
    }

    ReEvaluateClassifiedFiles();
};

private FencePanelViewModel CreateFenceForRule(ClassificationRule rule)
{
    // 计算位置
    double x = 200, y = 200;
    if (_fenceWindows.Count > 0)
    {
        var lastFence = _fenceWindows.Last();
        x = lastFence.Left + 50;
        y = lastFence.Top + 50;
    }

    // 创建新 Fence
    var definition = new FenceDefinition
    {
        Title = rule.Name,
        Bounds = new FenceRect { X = x, Y = y, Width = 300, Height = 200 },
        MonitorIndex = MonitorManager.GetMonitorIndexForPoint(x, y),
        PageIndex = _pageManager?.CurrentPageIndex ?? 0
    };

    // 更新规则的 TargetFenceId
    rule.TargetFenceId = definition.Id;
    _ = SaveRulesAsync();

    var vm = new FencePanelViewModel(definition);
    SpawnFenceWindow(vm);
    _pageManager?.AssignFenceToCurrentPage(definition.Id);
    RequestAutoSave();

    return vm;
}
```

## 影响范围
- 规则设置界面的目标Fence选择逻辑
- 规则保存时的自动创建Fence功能
- 自动整理和新文件处理时的Fence创建逻辑

## 验证标准
1. 禁用规则、删除Fence、重新启用规则，应该自动创建同名Fence
2. 在设置界面修改规则，目标Fence下拉框不应该自动选择第一个
3. 下拉框应该正确显示Fence标题而不是类型名
4. 自动整理和新文件功能应该仍然能正常工作
