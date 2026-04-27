# Phase 7: Tab 标签组

**目标**：将多个 Fence 合并为标签组，通过 Tab 切换不同 Fence 内容。

## 7.1 功能概述

FenceHost 支持将多个 Fence 合并为标签组（Tab Group），通过 Tab 切换不同 Fence 内容，类似浏览器多标签页。

**核心特性**：
- 多个 Fence 可合并到同一窗口，通过 Tab 切换
- Tab 分组状态自动持久化（TabGroupId + TabOrder）
- 重启后恢复 Tab 分组状态
- Tab 条右侧 "⋯" 菜单按钮：重命名、分离、Portal 设置、关闭
- 标题栏右侧 "⋯" 菜单按钮：重命名、Portal 设置、关闭（替代原右键菜单）

## 7.2 数据模型

**FenceDefinition 扩展字段**：
```csharp
public Guid? TabGroupId { get; set; }   // 所属标签组 ID（null 表示独立窗口）
public int TabOrder { get; set; }        // 在标签组内的顺序
```

**分组逻辑**：
- 首次合并时生成新的 `TabGroupId`
- 同一 `TabGroupId` 的 Fence 在启动时恢复到同一窗口
- `TabOrder` 决定 Tab 显示顺序

## 7.3 UI 结构

**FenceHost 窗口结构**：
```xml
<Grid Margin="4">
    <Grid.RowDefinitions>
        <RowDefinition x:Name="TabRow" Height="0" />  <!-- 单 Tab 时隐藏 -->
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <!-- Tab 条（2+ Tab 时显示） -->
    <Border x:Name="TabStripBorder" Grid.Row="0"
            Background="#CC1E1E2E"
            CornerRadius="8,8,0,0"
            BorderBrush="#55888888"
            BorderThickness="1,1,1,0"
            Margin="0,0,0,-1">
        <ItemsControl x:Name="TabStrip">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <StackPanel Orientation="Horizontal" />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
        </ItemsControl>
    </Border>

    <!-- 当前激活的 FencePanel -->
    <controls:FencePanel x:Name="FenceContent" Grid.Row="1" />
</Grid>
```

**Tab 按钮样式**：
- 激活 Tab：深蓝背景（`#99446699`），浅白文字（`#FFEEEEEE`）
- 非激活 Tab：灰色背景（`#33888888`），浅灰文字（`#AACCCCCC`）
- 字号 13px（与 Panel 标题一致）
- 高度 26px，圆角底部对齐

## 7.4 Tab 可见性与标题栏

**显示规则**：
- 单 Tab（`_tabs.Count == 1`）：TabRow 高度 = 0，`ShowTitleBar = true`
- 多 Tab（`_tabs.Count > 1`）：TabRow 高度 = 28，`ShowTitleBar = false`

## 7.5 Tab 交互

**点击切换**：
```csharp
btn.Click += (_, _) =>
{
    _activeTabIndex = idx;
    ActivatePanelForTab(idx);  // 切换 DataContext
    RefreshTabStrip();         // 重绘 Tab 样式
};
```

**"⋯" 菜单按钮**（Tab 条右侧 + 标题栏右侧）：
- Tab 条 "⋯" 按钮（`TabMenuButton`）：重命名、分离为独立 Fence、Portal 设置、关闭
- 标题栏 "⋯" 按钮（`TitleMenuButton`）：重命名、Portal 设置、关闭
- 替代了原先的 Tab 右键菜单和标题栏右键菜单，避免双击/拖拽事件冲突

## 7.6 合并与分离逻辑

**自动合并（位置重叠）**：
```csharp
private static bool FencesOverlapSignificantly(FenceHost a, FenceHost b)
{
    // 计算交集面积
    // 若交集 / 较小窗口面积 > 0.4，则触发合并
}
```

**手动合并（拖拽 Tab）**：
- 检测落点是否在目标窗口矩形内
- 更新 `TabGroupId` 和 `TabOrder`
- 调用 `sourceHost.RemoveTab()` + `targetHost.AddTab()`

**分离为独立窗口**：
```csharp
private void DetachTab(FenceHost host, FencePanelViewModel vm)
{
    host.RemoveTab(idx);
    vm.Model.TabGroupId = null;
    vm.Model.TabOrder = 0;

    // 偏移量 = vm.Width + 50，避免立即重新合并
    vm.X = host.Left + vm.Width + 50;
    vm.Y = host.Top + 50;

    var newHost = new FenceHost(_embedManager!, vm, _iconExtractor);
    // ... 设置事件，显示窗口
}
```

## 7.7 窗口尺寸同步

**Tab 条出现/消失时调整高度**：
```csharp
public void AddTab(FencePanelViewModel vm)
{
    bool wasTabbed = _tabs.Count > 1;
    _tabs.Add(vm);
    if (!wasTabbed)
        Height += 28;  // 首次出现 Tab 条
}

public FencePanelViewModel RemoveTab(int index)
{
    bool wasTabbed = _tabs.Count > 1;
    // ...
    if (wasTabbed && _tabs.Count == 1)
        Height -= 28;  // Tab 条消失
}
```

## 7.8 启动恢复

**SpawnFencesWithGroups 逻辑**：
```csharp
// 按 TabGroupId 分组
var grouped = definitions
    .Where(d => d.TabGroupId.HasValue)
    .GroupBy(d => d.TabGroupId!.Value)
    .ToDictionary(g => g.Key, g => g.OrderBy(d => d.TabOrder).ToList());

// 无分组 Fence 正常启动
foreach (var def in standalone)
    SpawnFenceWindow(new FencePanelViewModel(def));

// 分组 Fence：首个作为主窗口，其余作为 Tab 添加
foreach (var group in grouped.Values)
{
    var primaryVm = new FencePanelViewModel(group[0]);
    SpawnFenceWindow(primaryVm);
    var host = _fenceWindows.Last();
    for (int i = 1; i < group.Count; i++)
        host.AddTab(new FencePanelViewModel(group[i]));
}
```
