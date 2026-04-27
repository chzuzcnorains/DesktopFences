// Main app: fences state, tweaks, context menus, dialogs, search, peek
const { useState: uS, useRef: uR, useEffect: uE, useCallback: uCB } = React;

// ---------- Tweaks Panel ----------
const ACCENTS = [
  { name: 'blue',   hue: 248 },
  { name: 'indigo', hue: 275 },
  { name: 'teal',   hue: 195 },
  { name: 'green',  hue: 150 },
  { name: 'amber',  hue: 60 },
  { name: 'coral',  hue: 30 },
];

function TweaksPanel({ tweaks, setTweaks, open }) {
  if (!open) return null;
  const set = (k, v) => setTweaks({ ...tweaks, [k]: v });
  return (
    <div id="tweaks-panel" className="on">
      <div className="tweaks-head">
        Tweaks <span className="badge">视觉风格变体</span>
      </div>
      <div className="tweaks-body">
        <div className="tweak-row">
          <label>主题色</label>
          <div className="swatches">
            {ACCENTS.map(a => (
              <div key={a.name}
                className={"sw" + (tweaks.accent === a.hue ? " active" : "")}
                style={{background: `oklch(65% 0.15 ${a.hue})`}}
                onClick={() => set('accent', a.hue)} />
            ))}
          </div>
        </div>
        <div className="tweak-row">
          <label>背景色调 <span className="val">h={tweaks.bgHue}°</span></label>
          <input type="range" min="0" max="360" value={tweaks.bgHue}
                 onChange={e => set('bgHue', +e.target.value)} />
        </div>
        <div className="tweak-row">
          <label>Fence 透明度 <span className="val">{Math.round(tweaks.opacity * 100)}%</span></label>
          <input type="range" min="0.2" max="0.9" step="0.02" value={tweaks.opacity}
                 onChange={e => set('opacity', +e.target.value)} />
        </div>
        <div className="tweak-row">
          <label>模糊强度 (Acrylic) <span className="val">{tweaks.blur}px</span></label>
          <input type="range" min="0" max="60" step="1" value={tweaks.blur}
                 onChange={e => set('blur', +e.target.value)} />
        </div>
        <div className="tweak-row">
          <label>图标大小 <span className="val">{tweaks.iconSize}px</span></label>
          <input type="range" min="28" max="64" step="2" value={tweaks.iconSize}
                 onChange={e => set('iconSize', +e.target.value)} />
        </div>
        <div className="tweak-row">
          <label>标题栏样式</label>
          <div className="seg">
            {['flat','segmented','rounded','menuOnly'].map(s => (
              <button key={s} className={tweaks.tabStyle === s ? 'on' : ''}
                      onClick={() => set('tabStyle', s)}>
                {s === 'flat' ? 'Flat' : s === 'segmented' ? 'Segmented' : s === 'rounded' ? 'Rounded' : 'MenuOnly'}
              </button>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

// ---------- Context Menu ----------
function ContextMenu({ ctx, onClose, actions }) {
  if (!ctx) return null;
  uE(() => {
    // delay attachment so the right-click that opened us doesn't immediately close
    let attached = false;
    const close = (e) => {
      // ignore the same right-click that opened the menu
      if (e && e.type === 'contextmenu' && !attached) return;
      onClose();
    };
    const t = setTimeout(() => {
      attached = true;
      window.addEventListener('click', close);
      window.addEventListener('contextmenu', close);
    }, 0);
    return () => {
      clearTimeout(t);
      window.removeEventListener('click', close);
      window.removeEventListener('contextmenu', close);
    };
  }, [ctx.x, ctx.y, ctx.kind]);
  const x = Math.min(ctx.x, window.innerWidth - 220);
  const y = Math.min(ctx.y, window.innerHeight - 260);

  let items = [];
  if (ctx.kind === 'file') {
    items = [
      { label: '打开', icon: '↗', hint: 'Enter', onClick: () => actions.toast('打开: ' + ctx.file) },
      { label: '打开方式...', icon: '▸' },
      { sep: true },
      { label: '剪切', icon: '✂', hint: 'Ctrl+X' },
      { label: '复制', icon: '⎘', hint: 'Ctrl+C' },
      { label: '重命名', icon: '✎', onClick: () => actions.openRename(ctx) },
      { sep: true },
      { label: '移到桌面', icon: '⊟', onClick: () => actions.moveToDesktop(ctx) },
      { label: '从 Fence 移除', icon: '−', onClick: () => actions.removeFile(ctx) },
      { sep: true },
      { label: '删除', icon: '🗑', hint: 'Del', danger: true, onClick: () => actions.removeFile(ctx) },
      { label: '属性', icon: 'ⓘ' },
    ];
  } else if (ctx.kind === 'tab') {
    items = [
      { label: '重命名标签页...', icon: '✎', onClick: () => actions.openRenameTab(ctx) },
      { label: '分离为独立 Fence', icon: '⤴', onClick: () => actions.detachTab(ctx) },
      { label: '设为文件夹映射...', icon: '📁' },
      { sep: true },
      { label: '关闭标签页', icon: '×', danger: true, onClick: () => actions.closeTab(ctx) },
    ];
  } else if (ctx.kind === 'fence') {
    items = [
      { label: '重命名', icon: '✎', onClick: () => actions.openRenameFence(ctx) },
      { label: '新建标签页', icon: '+', onClick: () => actions.addTab(ctx) },
      { label: '设为文件夹映射...', icon: '📁' },
      { label: fenceById(actions.fences, ctx.fenceId)?.rolled ? '展开' : '折叠', icon: '▴', onClick: () => actions.toggleRollup(ctx) },
      { sep: true },
      { label: '主题颜色...', icon: '🎨', onClick: () => actions.openTweaks() },
      { label: '排序方式', icon: '↕' },
      { sep: true },
      { label: '关闭 Fence', icon: '×', danger: true, onClick: () => actions.closeFence(ctx) },
    ];
  } else if (ctx.kind === 'desktop') {
    const closed = actions.closedFences || [];
    items = [
      { label: '新建 Fence', icon: '+', onClick: () => actions.newFence(ctx) },
      { label: '立即整理桌面', icon: '⟲', onClick: () => actions.toast('已整理 3 个新增文件') },
      { sep: true },
    ];
    if (closed.length > 0) {
      items.push({ sectionLabel: `恢复最近关闭 (${closed.length})` });
      closed.slice(0, 5).forEach(cf => {
        const fc = cf.tabs.reduce((n,t)=>n+t.files.length,0);
        items.push({
          label: cf.title, icon: '↺',
          hint: `${cf.tabs.length}T · ${fc}项`,
          onClick: () => actions.restoreFence(cf.id),
        });
      });
      if (closed.length > 5) {
        items.push({ label: `查看全部 ${closed.length} 个...`, icon: '⋯', onClick: () => actions.openSettings('fences') });
      }
      items.push({ sep: true });
    }
    items.push(
      { label: '显示 / 隐藏所有 Fence', icon: '◎', hint: '双击桌面', onClick: () => actions.toggleHide() },
      { label: 'Peek 桌面', icon: '◉', hint: 'Win+Space', onClick: () => actions.togglePeek() },
      { label: '搜索...', icon: '⌕', hint: 'Ctrl+`', onClick: () => actions.openSearch() },
      { sep: true },
      { label: '布局快照', icon: '⧉' },
      { label: '分类规则...', icon: '⚙', onClick: () => actions.openSettings() },
      { label: '设置', icon: '⚙', onClick: () => actions.openSettings() },
    );
  }

  return (
    <div className="ctx-menu" style={{left: x, top: y}}>
      {items.map((it, i) => it.sep ? <div key={i} className="ctx-sep" /> : it.sectionLabel ? (
        <div key={i} className="ctx-section">{it.sectionLabel}</div>
      ) : (
        <div key={i} className={"ctx-item" + (it.danger ? " danger" : "")}
             onClick={() => { it.onClick && it.onClick(); onClose(); }}>
          <span className="ctx-icon">{it.icon}</span>
          <span className="ctx-label">{it.label}</span>
          {it.hint && <span className="ctx-hint">{it.hint}</span>}
        </div>
      ))}
    </div>
  );
}

function fenceById(fences, id) { return fences.find(f => f.id === id); }

// ---------- Rename dialog ----------
function RenameDialog({ ctx, onCommit, onClose }) {
  if (!ctx) return null;
  const [val, setVal] = uS(ctx.original);
  const inputRef = uR(null);
  uE(() => { inputRef.current?.focus(); inputRef.current?.select(); }, []);
  return (
    <div className="overlay" onClick={onClose}>
      <div className="window" onClick={(e) => e.stopPropagation()} style={{width: 460}}>
        <div className="window-head">
          重命名 {ctx.subject}
          <div className="close" onClick={onClose}>×</div>
        </div>
        <div className="window-body">
          <div style={{fontSize: 11, color: 'var(--text-dim)', marginBottom: 4}}>原名称</div>
          <div style={{padding: '6px 10px', background: 'rgba(255,255,255,0.04)', borderRadius: 5, marginBottom: 12, fontSize: 12, fontFamily: 'JetBrains Mono, monospace'}}>{ctx.original}</div>
          <div style={{fontSize: 11, color: 'var(--text-dim)', marginBottom: 4}}>新名称</div>
          <input ref={inputRef} className="input" value={val}
                 onChange={e => setVal(e.target.value)}
                 onKeyDown={e => {
                   if (e.key === 'Enter') onCommit(val);
                   if (e.key === 'Escape') onClose();
                 }} />
        </div>
        <div className="window-foot">
          <button className="btn" onClick={onClose}>取消</button>
          <button className="btn primary" onClick={() => onCommit(val)}>确认</button>
        </div>
      </div>
    </div>
  );
}

// ---------- Settings window ----------
const SETTINGS_NAV = [
  { key: 'general',   label: '常规',      icon: '⚙' },
  { key: 'appearance',label: '外观',      icon: '◐' },
  { key: 'rules',     label: '分类规则',   icon: '≡' },
  { key: 'fences',    label: 'Fence 管理', icon: '▦' },
  { key: 'shortcuts', label: '快捷键',    icon: '⌨' },
  { key: 'advanced',  label: '高级',      icon: '◆' },
  { key: 'about',     label: '关于',      icon: 'ⓘ' },
];

function SettingsWindow({ open, onClose, tweaks, setTweaks, fences, closedFences, onRestoreFence, onCreateFence }) {
  const [nav, setNav] = uS('general');
  const [q, setQ] = uS('');
  if (!open) return null;

  return (
    <div className="overlay" onClick={onClose}>
      <div className="settings-win" onClick={(e) => e.stopPropagation()}>
        {/* custom title bar */}
        <div className="sw-titlebar">
          <div className="sw-title">
            <svg width="14" height="14"><use href="#icon-app-logo"/></svg>
            <span>DesktopFences · 设置</span>
          </div>
          <div className="sw-wincontrols">
            <div className="sw-wc" title="最小化">–</div>
            <div className="sw-wc" title="最大化">▢</div>
            <div className="sw-wc close" onClick={onClose} title="关闭">×</div>
          </div>
        </div>

        <div className="sw-body">
          {/* sidebar */}
          <aside className="sw-side">
            <div className="sw-search">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>
              <input placeholder="搜索设置..." value={q} onChange={e=>setQ(e.target.value)} />
            </div>
            {SETTINGS_NAV.filter(n => !q || n.label.includes(q)).map(n => (
              <div key={n.key}
                className={"sw-nav" + (nav === n.key ? ' active' : '')}
                onClick={() => setNav(n.key)}>
                <span className="sw-nav-ic">{n.icon}</span>
                <span>{n.label}</span>
              </div>
            ))}
            <div className="sw-side-foot">
              <div style={{fontSize: 10.5, color: 'var(--text-faint)'}}>v0.9.0 · Build 2026.04</div>
            </div>
          </aside>

          {/* main content */}
          <main className="sw-main">
            {nav === 'general' && <GeneralPane />}
            {nav === 'appearance' && <AppearancePane tweaks={tweaks} setTweaks={setTweaks} />}
            {nav === 'rules' && <RulesPane />}
            {nav === 'fences' && <FencesManagePane fences={fences} closedFences={closedFences} onRestore={onRestoreFence} onCreate={onCreateFence} />}
            {nav === 'shortcuts' && <ShortcutsPane />}
            {nav === 'advanced' && <AdvancedPane />}
            {nav === 'about' && <AboutPane />}
          </main>
        </div>
      </div>
    </div>
  );
}

function SwCard({ title, desc, children, right }) {
  return (
    <div className="sw-card">
      <div className="sw-card-head">
        <div>
          <div className="sw-card-title">{title}</div>
          {desc && <div className="sw-card-desc">{desc}</div>}
        </div>
        {right && <div className="sw-card-right">{right}</div>}
      </div>
      {children && <div className="sw-card-body">{children}</div>}
    </div>
  );
}

function SwRow({ label, desc, right }) {
  return (
    <div className="sw-row">
      <div>
        <div className="sw-row-label">{label}</div>
        {desc && <div className="sw-row-desc">{desc}</div>}
      </div>
      <div className="sw-row-right">{right}</div>
    </div>
  );
}

function GeneralPane() {
  return (
    <div className="sw-pane">
      <PaneHeader title="常规" desc="启动、行为与基本偏好设置" />
      <SwCard title="启动">
        <SwRow label="开机自动启动" desc="写入 HKCU\\Run 注册表项" right={<Toggle />} />
        <SwRow label="启动时最小化到托盘" right={<Toggle defaultChecked />} />
        <SwRow label="启动时自动整理桌面" desc="扫描桌面未收纳文件并按规则分类" right={<Toggle defaultChecked />} />
      </SwCard>

      <SwCard title="交互行为">
        <SwRow label="双击桌面隐藏所有 Fence (Quick Hide)" right={<Toggle defaultChecked />} />
        <SwRow label="Snap 吸附距离" desc="拖动 Fence 时自动吸附到屏幕边缘和其它 Fence"
          right={<Slider min={0} max={30} value={10} unit="px" width={200} />} />
        <SwRow label="自动折叠延迟" desc="Rollup 折叠 Fence 的悬停展开延迟"
          right={<Slider min={100} max={1000} step={50} value={300} unit="ms" width={200} />} />
        <SwRow label="拖拽合并阈值" desc="重叠比例超过此值触发 Fence 合并为标签"
          right={<Slider min={20} max={70} value={35} unit="%" width={200} />} />
      </SwCard>

      <SwCard title="文件处理">
        <SwRow label="自动整理桌面新增文件" right={<Toggle defaultChecked />} />
        <SwRow label="虚拟化渲染 (大量文件时)" right={<Toggle defaultChecked />} />
        <SwRow label="图标缓存上限" right={<SwSelect options={['200','500','1000','2000']} value="500" />} />
      </SwCard>
    </div>
  );
}

function AppearancePane({ tweaks, setTweaks }) {
  const set = (k, v) => setTweaks({ ...tweaks, [k]: v });
  return (
    <div className="sw-pane">
      <PaneHeader title="外观" desc="主题色、透明度、标题栏样式与图标渲染" />

      {/* live preview */}
      <div className="sw-preview">
        <div className="sw-preview-label">实时预览</div>
        <div className="sw-preview-stage">
          <div className="sw-mini-fence">
            <div className="sw-mini-tabs">
              <div className={"sw-mini-tab active " + tweaks.tabStyle}>文档</div>
              <div className={"sw-mini-tab " + tweaks.tabStyle}>图片</div>
              <div className={"sw-mini-tab " + tweaks.tabStyle}>SQL</div>
            </div>
            <div className="sw-mini-body">
              {['doc','xls','img','ps1','sql','pdf'].map((k,i) => {
                const [a,b] = ICON_COLORS[k];
                return <div key={i} className="sw-mini-ico" style={{background:`linear-gradient(135deg,${a},${b})`, width: tweaks.iconSize*0.7, height: tweaks.iconSize*0.7}}/>;
              })}
            </div>
          </div>
        </div>
      </div>

      <SwCard title="主题色" desc="作用于激活标签、按钮和焦点高亮">
        <div className="sw-accents">
          {ACCENTS.map(a => (
            <div key={a.hue} className={"sw-accent " + (tweaks.accent === a.hue ? 'active' : '')}
                 style={{background: `oklch(65% 0.15 ${a.hue})`}}
                 onClick={() => set('accent', a.hue)}>
              {tweaks.accent === a.hue && <span>✓</span>}
            </div>
          ))}
        </div>
      </SwCard>

      <SwCard title="Fence 面板">
        <SwRow label="背景色相" right={<Slider min={0} max={360} value={tweaks.bgHue} unit="°" width={200}
                                          onChange={v => set('bgHue', v)} />} />
        <SwRow label="透明度" desc="Acrylic 背景不透明度" right={<Slider min={0.2} max={0.9} step={0.02}
                                          value={tweaks.opacity} unit="" width={200}
                                          fmt={v => Math.round(v*100)+'%'}
                                          onChange={v => set('opacity', v)} />} />
        <SwRow label="模糊强度" desc="backdrop-filter blur, 0 = 实色, 60 = 强模糊" right={<Slider min={0} max={60}
                                          value={tweaks.blur} unit="px" width={200}
                                          onChange={v => set('blur', v)} />} />
        <SwRow label="图标大小" right={<Slider min={28} max={64} step={2} value={tweaks.iconSize}
                                          unit="px" width={200} onChange={v => set('iconSize', v)} />} />
      </SwCard>

      <SwCard title="标题栏样式">
        <div className="sw-tabstyle-grid">
          {[
            { key: 'flat',      name: 'Flat',      desc: '下划线激活' },
            { key: 'segmented', name: 'Segmented', desc: '胶囊样式' },
            { key: 'rounded',   name: 'Rounded',   desc: '圆角底部' },
            { key: 'menuOnly',  name: 'MenuOnly',  desc: '极简菜单' },
          ].map(s => (
            <div key={s.key}
              className={"sw-tabstyle " + (tweaks.tabStyle === s.key ? 'active' : '')}
              onClick={() => set('tabStyle', s.key)}>
              <div className={"sw-tabstyle-preview " + s.key}>
                <span className="active">Tab</span>
                <span>Tab</span>
              </div>
              <div className="sw-tabstyle-name">{s.name}</div>
              <div className="sw-tabstyle-desc">{s.desc}</div>
            </div>
          ))}
        </div>
      </SwCard>
    </div>
  );
}

function RulesPane() {
  const [sel, setSel] = uS(0);
  const rules = [
    { p:1, name:'程序及快捷方式', match:'扩展名', pattern:'.exe, .lnk, .url, .bat, .cmd, .ps1, .msi', target:'程序及快捷方式', enabled:true },
    { p:2, name:'文件夹',         match:'是文件夹', pattern:'—',                                        target:'文件夹',         enabled:true },
    { p:3, name:'文档',           match:'扩展名', pattern:'.doc, .docx, .pdf, .txt, .md, .xlsx, .pptx', target:'文档',           enabled:true },
    { p:4, name:'视频',           match:'扩展名', pattern:'.mp4, .mkv, .avi, .mov, .webm',              target:'视频',           enabled:true },
    { p:5, name:'音乐',           match:'扩展名', pattern:'.mp3, .flac, .wav, .m4a, .ogg',              target:'音乐',           enabled:false },
    { p:6, name:'图片',           match:'扩展名', pattern:'.jpg, .png, .gif, .webp, .svg, .bmp',        target:'图片',           enabled:true },
  ];
  const r = rules[sel];

  return (
    <div className="sw-pane">
      <PaneHeader title="分类规则"
        desc="按优先级从小到大匹配, 第一个命中的规则决定目标 Fence"
        action={<><button className="btn">↻ 重新分类</button><button className="btn primary">+ 新建规则</button></>} />

      <div className="sw-rules">
        <div className="sw-rules-list">
          {rules.map((rule, i) => (
            <div key={i} className={"sw-rule " + (sel === i ? 'active' : '')} onClick={() => setSel(i)}>
              <div className="sw-rule-num">{rule.p}</div>
              <div className="sw-rule-main">
                <div className="sw-rule-name">{rule.name}</div>
                <div className="sw-rule-meta">{rule.match} → <b>{rule.target}</b></div>
              </div>
              <div className={"sw-rule-dot " + (rule.enabled ? 'on' : '')} />
            </div>
          ))}
        </div>
        <div className="sw-rules-detail">
          <div className="sw-rules-detail-head">
            <input className="input" defaultValue={r.name} />
            <div style={{display:'flex', gap:6}}>
              <button className="btn" title="上移">↑</button>
              <button className="btn" title="下移">↓</button>
              <button className="btn danger-btn">删除</button>
            </div>
          </div>
          <SwRow label="启用" right={<Toggle defaultChecked={r.enabled} />} />
          <SwRow label="优先级" right={<input className="input" style={{width:80}} defaultValue={r.p} />} />
          <SwRow label="匹配方式" right={
            <SwSelect value={r.match} options={['扩展名','名称 Glob','正则表达式','日期范围','大小范围','是文件夹']} />
          } />
          <SwRow label="匹配模式" desc="逗号分隔, 如 .jpg, .png, .gif"
            right={<input className="input" style={{width:260}} defaultValue={r.pattern} />} />
          <SwRow label="目标 Fence" right={<SwSelect value={r.target} options={['程序及快捷方式','文件夹','文档','视频','音乐','图片','新建 Fence...']} />} />
        </div>
      </div>
    </div>
  );
}

function FencesManagePane({ fences, closedFences = [], onRestore, onCreate }) {
  const [section, setSection] = uS('active');
  return (
    <div className="sw-pane">
      <PaneHeader title="Fence 管理"
        desc="活动 Fence 一览; 关闭的 Fence 会保留在回收站, 随时可以恢复"
        action={<button className="btn primary" onClick={() => onCreate && onCreate({})}>+ 新建 Fence</button>} />

      <div className="sw-fence-segmented">
        <div className={"sw-seg " + (section === 'active' ? 'active' : '')} onClick={() => setSection('active')}>
          活动 <span className="sw-seg-count">{fences.length}</span>
        </div>
        <div className={"sw-seg " + (section === 'closed' ? 'active' : '')} onClick={() => setSection('closed')}>
          最近关闭 <span className="sw-seg-count">{closedFences.length}</span>
        </div>
      </div>

      {section === 'closed' ? (
        <ClosedFencesList closedFences={closedFences} onRestore={onRestore} />
      ) : (<>
      <div className="sw-fence-list">
        <div className="sw-fence-head">
          <span>名称</span>
          <span>Tab 数</span>
          <span>文件总数</span>
          <span>位置</span>
          <span>尺寸</span>
          <span>操作</span>
        </div>
        {fences.map(f => (
          <div key={f.id} className="sw-fence-row">
            <span style={{fontWeight:500}}>{f.title}{f.rolled ? <span style={{color:'var(--text-faint)',marginLeft:6,fontSize:11}}>(已折叠)</span> : null}</span>
            <span>{f.tabs.length}</span>
            <span>{f.tabs.reduce((n,t)=>n+t.files.length,0)}</span>
            <span className="mono">{f.x},{f.y}</span>
            <span className="mono">{f.w}×{f.h}</span>
            <span style={{color:'var(--text-dim)'}}>⋯</span>
          </div>
        ))}
      </div>
      <div style={{marginTop: 14, display:'flex', gap: 6}}>
        <button className="btn">导出布局...</button>
        <button className="btn">导入布局...</button>
        <button className="btn">保存快照</button>
      </div>
      </>)}
    </div>
  );
}

function ClosedFencesList({ closedFences, onRestore }) {
  if (!closedFences || closedFences.length === 0) {
    return (
      <div className="sw-empty">
        <div className="sw-empty-ico">
          <svg width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
        </div>
        <div className="sw-empty-title">回收站为空</div>
        <div className="sw-empty-desc">关闭 Fence 后会出现在这里, 7 天内可一键恢复</div>
      </div>
    );
  }
  const fmtWhen = ts => {
    const d = Math.floor((Date.now() - ts) / 1000);
    if (d < 60) return d + ' 秒前';
    if (d < 3600) return Math.floor(d/60) + ' 分钟前';
    if (d < 86400) return Math.floor(d/3600) + ' 小时前';
    return Math.floor(d/86400) + ' 天前';
  };
  return (
    <div className="sw-closed-grid">
      {closedFences.map(cf => {
        const fileCount = cf.tabs.reduce((n,t)=>n+t.files.length,0);
        return (
          <div key={cf.id} className="sw-closed-card">
            <div className="sw-closed-preview">
              <div className="sw-closed-mini-head">
                {cf.tabs.slice(0, 4).map((t, i) => (
                  <div key={i} className={"sw-closed-mini-tab" + (i === 0 ? ' active' : '')}>{t.title}</div>
                ))}
                {cf.tabs.length > 4 && <div className="sw-closed-mini-tab">+{cf.tabs.length - 4}</div>}
              </div>
              <div className="sw-closed-mini-body">
                {Array.from({length: Math.min(8, fileCount || 3)}).map((_, i) => (
                  <div key={i} className="sw-closed-mini-ico" style={{
                    background: `linear-gradient(135deg, oklch(65% 0.12 ${200 + i*22}), oklch(50% 0.14 ${200 + i*22}))`
                  }}/>
                ))}
              </div>
            </div>
            <div className="sw-closed-meta">
              <div>
                <div className="sw-closed-name">{cf.title}</div>
                <div className="sw-closed-sub">
                  <span>{cf.tabs.length} Tab</span>
                  <span>·</span>
                  <span>{fileCount} 文件</span>
                  <span>·</span>
                  <span>{fmtWhen(cf.closedAt)}</span>
                </div>
              </div>
              <button className="btn primary" onClick={() => onRestore(cf.id)}>恢复</button>
            </div>
          </div>
        );
      })}
    </div>
  );
}

function ShortcutsPane() {
  const items = [
    { group: '窗口', rows: [
      ['显示 / 隐藏所有 Fence', ['双击桌面']],
      ['Peek · 所有 Fence 置顶', ['Win','Space']],
      ['显示桌面 (系统)', ['Win','D']],
    ]},
    { group: '导航', rows: [
      ['快捷搜索', ['Ctrl','`']],
      ['切换下一个 Tab', ['Ctrl','Tab']],
      ['切换上一个 Tab', ['Ctrl','Shift','Tab']],
    ]},
    { group: '操作', rows: [
      ['新建 Fence', ['桌面右键']],
      ['重命名', ['F2']],
      ['删除选中', ['Del']],
      ['全选', ['Ctrl','A']],
    ]},
  ];
  return (
    <div className="sw-pane">
      <PaneHeader title="快捷键" desc="点击可重新绑定 (演示用)" />
      {items.map(g => (
        <SwCard key={g.group} title={g.group}>
          {g.rows.map(([k, keys]) => (
            <SwRow key={k} label={k} right={
              <div style={{display:'flex', gap:4}}>
                {keys.map((x,i) => <span key={i} className="kbd-big">{x}</span>)}
              </div>
            } />
          ))}
        </SwCard>
      ))}
    </div>
  );
}

function AdvancedPane() {
  return (
    <div className="sw-pane">
      <PaneHeader title="高级" desc="需要重启生效的底层设置" />
      <SwCard title="桌面嵌入">
        <SwRow label="Z-order 管理" desc="正常态 HWND_BOTTOM, Win+D 后 HWND_TOPMOST" right={<Toggle defaultChecked />} />
        <SwRow label="兼容模式" desc="禁用 z-order 管理, 与其它桌面增强工具共存" right={<Toggle />} />
        <SwRow label="Win+D 检测延迟" right={<Slider min={0} max={800} step={50} value={300} unit="ms" width={200} />} />
      </SwCard>
      <SwCard title="诊断">
        <SwRow label="调试日志" desc="输出到 %APPDATA%\\DesktopFences\\log\\" right={<Toggle />} />
        <SwRow label="日志等级" right={<SwSelect value="Info" options={['Error','Warn','Info','Debug','Trace']} />} />
        <SwRow label="打开日志文件夹" right={<button className="btn">打开</button>} />
      </SwCard>
      <SwCard title="危险操作" desc="谨慎操作, 以下动作不可撤销">
        <SwRow label="重置所有 Fence 布局" right={<button className="btn danger-btn">重置</button>} />
        <SwRow label="清空分类规则" right={<button className="btn danger-btn">清空</button>} />
        <SwRow label="恢复所有默认设置" right={<button className="btn danger-btn">恢复</button>} />
      </SwCard>
    </div>
  );
}

function AboutPane() {
  return (
    <div className="sw-pane">
      <div className="sw-about-hero">
        <div className="sw-about-logo">
          <svg width="48" height="48"><use href="#icon-app-logo"/></svg>
        </div>
        <div>
          <div className="sw-about-name">DesktopFences</div>
          <div className="sw-about-ver">0.9.0 · Build 2026.04 · Windows x64</div>
        </div>
      </div>
      <div className="sw-about-desc">
        对标 Stardock Fences 的开源桌面整理工具. 通过横向 Fence 分组, 自动分类规则, Tab 合并和 Folder Portal 高效管理桌面文件.
      </div>
      <div className="sw-about-stats">
        <div><b>{/* */}5</b><span>活动 Fence</span></div>
        <div><b>42</b><span>已管理文件</span></div>
        <div><b>6</b><span>分类规则</span></div>
        <div><b>13 天</b><span>运行时长</span></div>
      </div>
      <div className="sw-about-links">
        <a>更新日志</a><a>检查更新</a><a>GitHub</a><a>反馈问题</a><a>开源许可</a>
      </div>
    </div>
  );
}

function PaneHeader({ title, desc, action }) {
  return (
    <div className="sw-pane-head">
      <div>
        <div className="sw-pane-title">{title}</div>
        {desc && <div className="sw-pane-desc">{desc}</div>}
      </div>
      {action && <div className="sw-pane-action">{action}</div>}
    </div>
  );
}

function Slider({ min, max, step=1, value, unit='', width=160, fmt, onChange }) {
  const [v, setV] = uS(value);
  uE(() => setV(value), [value]);
  return (
    <div className="sw-slider" style={{width}}>
      <input type="range" min={min} max={max} step={step} value={v}
             onChange={e => { const nv = +e.target.value; setV(nv); onChange && onChange(nv); }} />
      <span className="sw-slider-val">{fmt ? fmt(v) : (v + unit)}</span>
    </div>
  );
}

function SwSelect({ options, value, onChange }) {
  return (
    <select className="select sw-select" defaultValue={value} onChange={e => onChange && onChange(e.target.value)}>
      {options.map(o => <option key={o} value={o}>{o}</option>)}
    </select>
  );
}
function Toggle({ defaultChecked }) {
  const [on, set] = uS(!!defaultChecked);
  return (
    <div onClick={() => set(!on)} className={"sw-toggle" + (on ? " on" : "")}>
      <div className="sw-toggle-dot" />
    </div>
  );
}

// ---------- Search palette ----------
function SearchPalette({ open, onClose, fences }) {
  const [q, setQ] = uS('');
  const [sel, setSel] = uS(0);
  const inputRef = uR(null);
  uE(() => { if (open) { inputRef.current?.focus(); setQ(''); setSel(0); } }, [open]);
  if (!open) return null;

  const all = [];
  fences.forEach(f => f.tabs.forEach(t => t.files.forEach(file => {
    all.push({ file, fence: f.title, tab: t.title, fenceId: f.id });
  })));
  const results = q.trim()
    ? all.filter(r => r.file.toLowerCase().includes(q.toLowerCase()) || r.fence.toLowerCase().includes(q.toLowerCase())).slice(0, 30)
    : all.slice(0, 10);

  return (
    <div className="overlay" onClick={onClose}>
      <div id="search" className="on" onClick={e => e.stopPropagation()}>
        <input ref={inputRef} placeholder="搜索所有 Fence 中的文件..."
          value={q}
          onChange={e => { setQ(e.target.value); setSel(0); }}
          onKeyDown={e => {
            if (e.key === 'Escape') onClose();
            if (e.key === 'ArrowDown') setSel(s => Math.min(results.length - 1, s + 1));
            if (e.key === 'ArrowUp') setSel(s => Math.max(0, s - 1));
          }} />
        <div className="results">
          {results.length === 0 ? (
            <div style={{padding: 20, textAlign: 'center', color: 'var(--text-faint)', fontSize: 12}}>无匹配结果</div>
          ) : results.map((r, i) => {
            const info = iconFor(r.file);
            const [a, b] = ICON_COLORS[info.kind] || ICON_COLORS.txt;
            return (
              <div key={i} className={"res" + (i === sel ? ' sel' : '')} onMouseEnter={() => setSel(i)}>
                <div className="ico-s" style={{background: `linear-gradient(135deg, ${a}, ${b})`}}>{info.label}</div>
                <div className="rn">{r.file}</div>
                <div className="rt">{r.fence}{r.fence !== r.tab ? ' · ' + r.tab : ''}</div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

// ---------- App ----------
function App() {
  const [fences, setFences] = uS(() => {
    try {
      const saved = localStorage.getItem('df_fences_v1');
      if (saved) return JSON.parse(saved);
    } catch (e) {}
    return INITIAL_FENCES;
  });
  const [closedFences, setClosedFences] = uS(() => {
    try {
      const saved = localStorage.getItem('df_closed_v1');
      if (saved) return JSON.parse(saved);
    } catch (e) {}
    return [];
  });
  const [focusId, setFocusId] = uS(fences[0]?.id);
  const [tweaks, setTweaks] = uS(() => {
    try {
      const saved = localStorage.getItem('df_tweaks_v1');
      if (saved) return JSON.parse(saved);
    } catch (e) {}
    return { accent: 248, bgHue: 248, opacity: 0.55, blur: 26, iconSize: 44, tabStyle: 'flat' };
  });
  const [ctxMenu, setCtxMenu] = uS(null);
  const [rename, setRename] = uS(null);
  const [settingsOpen, setSettingsOpen] = uS(false);
  const [searchOpen, setSearchOpen] = uS(false);
  const [tweaksOpen, setTweaksOpen] = uS(true);
  const [peek, setPeek] = uS(false);
  const [hidden, setHidden] = uS(false);
  const [mergeTargetId, setMergeTargetId] = uS(null);

  // Fence drag: detect overlap to highlight merge target, and merge on drop
  uE(() => {
    const rectOf = (f) => ({ l: f.x, t: f.y, r: f.x + f.w, b: f.y + f.h });
    const overlapArea = (a, b) => {
      const w = Math.max(0, Math.min(a.r, b.r) - Math.max(a.l, b.l));
      const h = Math.max(0, Math.min(a.b, b.b) - Math.max(a.t, b.t));
      return w * h;
    };
    const onMove = (e) => {
      const d = e.detail;
      const src = { l: d.x, t: d.y, r: d.x + d.w, b: d.y + d.h };
      const srcArea = d.w * d.h;
      let best = null, bestRatio = 0;
      fences.forEach(f => {
        if (f.id === d.id) return;
        const fr = rectOf({ ...f, h: f.rolled ? 34 : f.h });
        const ar = overlapArea(src, fr);
        const ratio = ar / Math.min(srcArea, f.w * (f.rolled ? 34 : f.h));
        if (ratio > 0.35 && ratio > bestRatio) { best = f.id; bestRatio = ratio; }
      });
      setMergeTargetId(best);
    };
    const onEnd = (e) => {
      const d = e.detail;
      if (mergeTargetId && mergeTargetId !== d.id) {
        // merge source fence's tabs into target
        setFences(fs => {
          const src = fs.find(f => f.id === d.id);
          const tgt = fs.find(f => f.id === mergeTargetId);
          if (!src || !tgt) return fs;
          const merged = { ...tgt, tabs: [...tgt.tabs, ...src.tabs], activeTab: tgt.tabs.length };
          return fs.filter(f => f.id !== d.id).map(f => f.id === tgt.id ? merged : f);
        });
        const t = document.getElementById('toast');
        if (t) { t.textContent = '已合并为标签页'; t.classList.add('on'); clearTimeout(window.__toastT); window.__toastT = setTimeout(() => t.classList.remove('on'), 1800); }
      }
      setMergeTargetId(null);
    };
    window.addEventListener('fence-drag-move', onMove);
    window.addEventListener('fence-drag-end', onEnd);
    return () => {
      window.removeEventListener('fence-drag-move', onMove);
      window.removeEventListener('fence-drag-end', onEnd);
    };
  }, [fences, mergeTargetId]);

  uE(() => {
    try { localStorage.setItem('df_fences_v1', JSON.stringify(fences)); } catch (e) {}
  }, [fences]);
  uE(() => {
    try { localStorage.setItem('df_tweaks_v1', JSON.stringify(tweaks)); } catch (e) {}
    // apply tweaks as CSS vars
    const r = document.documentElement;
    r.style.setProperty('--accent', `oklch(72% 0.12 ${tweaks.accent})`);
    r.style.setProperty('--accent-hue', tweaks.accent);
    r.style.setProperty('--blur', tweaks.blur + 'px');
    r.style.setProperty('--icon-size', tweaks.iconSize + 'px');
    const tile = Math.max(72, tweaks.iconSize + 44);
    r.style.setProperty('--tile-w', tile + 'px');
    r.style.setProperty('--tile-h', (tweaks.iconSize + 52) + 'px');
    r.style.setProperty('--fence-bg', `oklch(20% 0.04 ${tweaks.bgHue} / ${tweaks.opacity})`);
    r.style.setProperty('--titlebar-active', `oklch(55% 0.12 ${tweaks.accent} / 0.35)`);
    r.setAttribute('data-tabstyle', tweaks.tabStyle);
  }, [tweaks]);

  uE(() => {
    try { localStorage.setItem('df_closed_v1', JSON.stringify(closedFences)); } catch (e) {}
  }, [closedFences]);

  const restoreFence = (closedId) => {
    const cf = closedFences.find(c => c.id === closedId);
    if (!cf) return;
    setFences(fs => [...fs, { ...cf, closedAt: undefined, x: (cf.x || 60) + 20, y: (cf.y || 60) + 20 }]);
    setClosedFences(cs => cs.filter(c => c.id !== closedId));
    toast('已恢复: ' + cf.title);
  };

  const createFence = (preset = {}) => {
    const id = 'f' + Date.now();
    setFences(fs => [...fs, {
      id, title: preset.title || '新建 Fence',
      x: preset.x ?? 120, y: preset.y ?? 120, w: preset.w ?? 420, h: preset.h ?? 180,
      rolled: false,
      tabs: [{ id: 't'+Date.now(), title: preset.title || '新建 Fence', files: [] }],
      activeTab: 0,
    }]);
    toast('已新建 Fence');
  };

  // Keyboard shortcuts
  uE(() => {
    const onKey = (e) => {
      const meta = e.metaKey || e.ctrlKey;
      if (meta && e.key === '`') { e.preventDefault(); setSearchOpen(o => !o); }
      if (e.key === 'Escape') { setSearchOpen(false); setCtxMenu(null); setPeek(false); }
      // Win+Space simulated with Alt+Space for browser compat
      if ((e.key === ' ' || e.code === 'Space') && e.altKey) { e.preventDefault(); setPeek(p => !p); }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  // Tweaks panel toggle via toolbar (Alt+T as convenience here)
  uE(() => {
    window.addEventListener('message', (ev) => {
      if (ev.data?.type === '__activate_edit_mode') setTweaksOpen(true);
      if (ev.data?.type === '__deactivate_edit_mode') setTweaksOpen(false);
    });
    window.addEventListener('message', () => {});
    // register tweaks listener first, then announce
    setTimeout(() => window.parent.postMessage({type: '__edit_mode_available'}, '*'), 0);
  }, []);

  // Clock
  uE(() => {
    const upd = () => {
      const d = new Date();
      const hh = String(d.getHours()).padStart(2,'0');
      const mm = String(d.getMinutes()).padStart(2,'0');
      const el = document.getElementById('clock');
      if (el) el.innerHTML = `<b>${hh}:${mm}</b><span>${d.getFullYear()}/${String(d.getMonth()+1).padStart(2,'0')}/${String(d.getDate()).padStart(2,'0')}</span>`;
    };
    upd(); const t = setInterval(upd, 15000); return () => clearInterval(t);
  }, []);

  // Native icons render
  uE(() => {
    const host = document.getElementById('native-icons');
    if (!host) return;
    host.innerHTML = '';
    NATIVE_ICONS.forEach(n => {
      const [a, b] = ICON_COLORS[n.kind] || ICON_COLORS.txt;
      const el = document.createElement('div');
      el.className = 'nat-icon';
      el.innerHTML = `<div class="ic" style="background:linear-gradient(135deg,${a},${b})">${n.label}</div><div class="nm">${n.name}</div>`;
      host.appendChild(el);
    });
  }, []);

  // Double-click desktop = toggle hide
  uE(() => {
    const root = document.getElementById('desktop');
    const onDbl = (e) => {
      if (e.target.closest('.fence-wrap, #taskbar, #native-icons, #tweaks-panel, .overlay, .ctx-menu')) return;
      setHidden(h => !h);
      toast(hidden ? '显示所有 Fence' : '隐藏所有 Fence · 双击桌面恢复');
    };
    const onCtx = (e) => {
      if (e.target.closest('.fence-wrap, #taskbar, .overlay, .ctx-menu, #tweaks-panel')) return;
      e.preventDefault();
      e.stopPropagation();
      setCtxMenu({ kind: 'desktop', x: e.clientX, y: e.clientY });
    };
    root.addEventListener('dblclick', onDbl);
    root.addEventListener('contextmenu', onCtx);
    return () => {
      root.removeEventListener('dblclick', onDbl);
      root.removeEventListener('contextmenu', onCtx);
    };
  }, [hidden]);

  uE(() => {
    document.body.classList.toggle('hidden-mode', hidden);
    document.body.classList.toggle('peek-mode', peek);
    document.getElementById('peek-indicator').classList.toggle('on', peek);
  }, [hidden, peek]);

  const toast = (msg) => {
    const t = document.getElementById('toast');
    t.textContent = msg;
    t.classList.add('on');
    clearTimeout(window.__toastT);
    window.__toastT = setTimeout(() => t.classList.remove('on'), 1800);
  };

  const updateFence = (id, patch) => {
    setFences(fs => fs.map(f => f.id === id ? { ...f, ...patch } : f));
  };

  const onContextMenu = (e, data) => {
    e.preventDefault(); e.stopPropagation();
    setCtxMenu({ ...data, x: e.clientX, y: e.clientY });
  };

  const onDragFile = (e, payload) => {
    e.dataTransfer.setData('application/x-file', JSON.stringify(payload));
    e.dataTransfer.effectAllowed = 'move';
    // custom ghost
    const g = document.createElement('div');
    g.className = 'drag-ghost';
    g.textContent = payload.file;
    document.body.appendChild(g);
    e.dataTransfer.setDragImage(g, 10, 10);
    setTimeout(() => g.remove(), 0);
  };

  const onDropFile = (payload, toFenceId, toTabId) => {
    if (payload.fenceId === toFenceId && payload.tabId === toTabId) return;
    setFences(fs => fs.map(f => {
      const newTabs = f.tabs.map(t => {
        // remove from source
        if (f.id === payload.fenceId && t.id === payload.tabId) {
          return { ...t, files: t.files.filter(x => x !== payload.file) };
        }
        // add to target
        if (f.id === toFenceId && t.id === toTabId) {
          if (t.files.includes(payload.file)) return t;
          return { ...t, files: [...t.files, payload.file] };
        }
        return t;
      });
      return { ...f, tabs: newTabs };
    }));
    toast(`已移动: ${payload.file}`);
  };

  const actions = {
    fences,
    closedFences,
    toast,
    restoreFence,
    openSettings: () => setSettingsOpen(true),
    openSearch: () => setSearchOpen(true),
    openTweaks: () => setTweaksOpen(true),
    togglePeek: () => setPeek(p => !p),
    toggleHide: () => setHidden(h => !h),
    newFence: (ctx) => {
      const id = 'f' + Date.now();
      setFences(fs => [...fs, {
        id, title: '新建 Fence',
        x: ctx.x - 200, y: ctx.y - 20, w: 360, h: 160,
        rolled: false,
        tabs: [{ id: 't' + Date.now(), title: '新建 Fence', files: [] }],
        activeTab: 0,
      }]);
      toast('新建 Fence');
    },
    closeFence: (ctx) => {
      const f = fences.find(x => x.id === ctx.fenceId);
      if (f) setClosedFences(cs => [{ ...f, closedAt: Date.now() }, ...cs].slice(0, 50));
      setFences(fs => fs.filter(f => f.id !== ctx.fenceId));
      toast('Fence 已关闭 · 可在 Fence 管理中恢复');
    },
    closeTab: (ctx) => {
      setFences(fs => fs.map(f => {
        if (f.id !== ctx.fenceId) return f;
        if (f.tabs.length <= 1) return f;
        const tabs = f.tabs.filter((_, i) => i !== ctx.tabIdx);
        return { ...f, tabs, activeTab: Math.min(f.activeTab, tabs.length - 1) };
      }));
    },
    detachTab: (ctx) => {
      const src = fenceById(fences, ctx.fenceId);
      if (!src || src.tabs.length <= 1) return toast('单标签页无法分离');
      const tab = src.tabs[ctx.tabIdx];
      const newFence = {
        id: 'f' + Date.now(), title: tab.title,
        x: src.x + 40, y: src.y + 40, w: 320, h: 180,
        rolled: false, tabs: [tab], activeTab: 0,
      };
      setFences(fs => [
        ...fs.map(f => f.id === ctx.fenceId ? { ...f, tabs: f.tabs.filter((_, i) => i !== ctx.tabIdx), activeTab: 0 } : f),
        newFence,
      ]);
      toast('已分离为独立 Fence');
    },
    addTab: (ctx) => {
      setFences(fs => fs.map(f => f.id === ctx.fenceId
        ? { ...f, tabs: [...f.tabs, { id: 't'+Date.now(), title: '新标签', files: [] }], activeTab: f.tabs.length }
        : f));
    },
    toggleRollup: (ctx) => {
      setFences(fs => fs.map(f => f.id === ctx.fenceId ? { ...f, rolled: !f.rolled } : f));
    },
    openRename: (ctx) => setRename({ subject: '文件', original: ctx.file, commit: (val) => {
      setFences(fs => fs.map(f => f.id === ctx.fenceId
        ? { ...f, tabs: f.tabs.map(t => t.id === ctx.tabId ? { ...t, files: t.files.map(x => x === ctx.file ? val : x) } : t) }
        : f));
      toast('已重命名');
    }}),
    openRenameFence: (ctx) => {
      const f = fenceById(fences, ctx.fenceId);
      setRename({ subject: 'Fence', original: f.title, commit: (val) => {
        updateFence(ctx.fenceId, { title: val, tabs: f.tabs.length === 1 ? [{ ...f.tabs[0], title: val }] : f.tabs });
      }});
    },
    openRenameTab: (ctx) => {
      const f = fenceById(fences, ctx.fenceId);
      const tab = f.tabs[ctx.tabIdx];
      setRename({ subject: '标签页', original: tab.title, commit: (val) => {
        setFences(fs => fs.map(ff => ff.id !== ctx.fenceId ? ff : {
          ...ff, tabs: ff.tabs.map((t, i) => i === ctx.tabIdx ? { ...t, title: val } : t)
        }));
      }});
    },
    removeFile: (ctx) => {
      setFences(fs => fs.map(f => f.id !== ctx.fenceId ? f : {
        ...f, tabs: f.tabs.map(t => t.id !== ctx.tabId ? t : { ...t, files: t.files.filter(x => x !== ctx.file) })
      }));
      toast('已删除: ' + ctx.file);
    },
    moveToDesktop: (ctx) => {
      setFences(fs => fs.map(f => f.id !== ctx.fenceId ? f : {
        ...f, tabs: f.tabs.map(t => t.id !== ctx.tabId ? t : { ...t, files: t.files.filter(x => x !== ctx.file) })
      }));
      toast('已移回桌面');
    },
  };

  return (
    <>
      {fences.map(f => (
        <Fence key={f.id} fence={f}
          focused={focusId === f.id}
          mergeTargetId={mergeTargetId}
          onFocus={() => setFocusId(f.id)}
          onUpdate={(patch) => updateFence(f.id, patch)}
          onContextMenu={onContextMenu}
          onDropFile={onDropFile}
          onDragFile={onDragFile}
        />
      ))}
      <TweaksPanel tweaks={tweaks} setTweaks={setTweaks} open={tweaksOpen} />
      <ContextMenu ctx={ctxMenu} onClose={() => setCtxMenu(null)} actions={actions} />
      {rename && <RenameDialog ctx={rename} onClose={() => setRename(null)}
                    onCommit={(val) => { rename.commit(val); setRename(null); }} />}
      <SettingsWindow open={settingsOpen} onClose={() => setSettingsOpen(false)}
                      tweaks={tweaks} setTweaks={setTweaks} fences={fences}
                      closedFences={closedFences}
                      onRestoreFence={restoreFence}
                      onCreateFence={createFence} />
      <SearchPalette open={searchOpen} onClose={() => setSearchOpen(false)} fences={fences} />
    </>
  );
}

window.openSearch = () => {
  // Fired from taskbar search pill
  const ev = new KeyboardEvent('keydown', { key: '`', ctrlKey: true });
  window.dispatchEvent(ev);
};

ReactDOM.createRoot(document.getElementById('fences-root')).render(<App />);
