// Fence component + file tile
const { useState, useRef, useEffect, useCallback } = React;

function FileTile({ file, fenceId, tabId, selected, onSelect, onContextMenu, onDragStart }) {
  const info = iconFor(file);
  const [a, b] = ICON_COLORS[info.kind] || ICON_COLORS.txt;
  return (
    <div
      className={"file" + (selected ? " selected" : "")}
      draggable
      onClick={(e) => onSelect(e, file)}
      onContextMenu={(e) => onContextMenu(e, { kind: 'file', file, fenceId, tabId })}
      onDragStart={(e) => onDragStart(e, { file, fenceId, tabId })}
      title={file}
    >
      <div className="ico" style={{background: `linear-gradient(135deg, ${a}, ${b})`}}>
        {info.label}
      </div>
      <div className="nm">{file}</div>
    </div>
  );
}

function Tab({ tab, idx, active, onClick, onClose, onContextMenu, canClose }) {
  return (
    <div
      className={"tab" + (active ? " active" : "")}
      onClick={() => onClick(idx)}
      onContextMenu={(e) => onContextMenu(e, idx)}
      title={tab.title}
    >
      <span className="tab-label">{tab.title}</span>
      {canClose && (
        <span className="tab-close" onClick={(e) => { e.stopPropagation(); onClose(idx); }}>×</span>
      )}
    </div>
  );
}

function Fence({ fence, focused, onFocus, onUpdate, onContextMenu, onDropFile, onDragFile, onMergeInto, mergeTargetId }) {
  const wrapRef = useRef(null);
  const [selected, setSelected] = useState(new Set());
  const [dropHover, setDropHover] = useState(false);
  const [dropped, setDropped] = useState(false);

  const activeTab = fence.tabs[fence.activeTab] || fence.tabs[0];
  const multiTab = fence.tabs.length > 1;

  // dragging the fence (title bar / tabstrip)
  const dragRef = useRef(null);
  const startDrag = (e) => {
    if (e.button !== 0) return;
    if (e.target.closest('.tab-close, .ts-btn, .tab')) {
      // allow tab clicks; but drag if on empty strip area
      if (!e.target.closest('.tabstrip') || e.target.closest('.tab, .ts-btn')) return;
    }
    onFocus();
    const startX = e.clientX, startY = e.clientY;
    const startLeft = fence.x, startTop = fence.y;
    let lastPos = { x: startLeft, y: startTop };
    const move = (ev) => {
      const nx = Math.max(0, Math.min(window.innerWidth - 100, startLeft + (ev.clientX - startX)));
      const ny = Math.max(0, Math.min(window.innerHeight - 60, startTop + (ev.clientY - startY)));
      lastPos = { x: nx, y: ny };
      onUpdate({ x: nx, y: ny });
      // broadcast pointer so App can compute overlap preview
      window.dispatchEvent(new CustomEvent('fence-drag-move', { detail: { id: fence.id, x: nx, y: ny, w: fence.w, h: fence.rolled ? 34 : fence.h } }));
    };
    const up = () => {
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', up);
      window.dispatchEvent(new CustomEvent('fence-drag-end', { detail: { id: fence.id, ...lastPos, w: fence.w, h: fence.rolled ? 34 : fence.h } }));
    };
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
  };

  // resize
  const startResize = (e, dir) => {
    e.stopPropagation();
    const sx = e.clientX, sy = e.clientY;
    const sw = fence.w, sh = fence.h;
    const move = (ev) => {
      const nw = dir.includes('r') ? Math.max(260, sw + (ev.clientX - sx)) : sw;
      const nh = dir.includes('b') ? Math.max(80, sh + (ev.clientY - sy)) : sh;
      onUpdate({ w: nw, h: nh });
    };
    const up = () => {
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', up);
    };
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
  };

  const handleSelect = (e, f) => {
    const next = new Set(selected);
    if (e.ctrlKey || e.metaKey) {
      if (next.has(f)) next.delete(f); else next.add(f);
    } else {
      next.clear();
      next.add(f);
    }
    setSelected(next);
  };

  const handleTabClick = (i) => onUpdate({ activeTab: i });
  const handleTabClose = (i) => {
    if (fence.tabs.length <= 1) return;
    const tabs = fence.tabs.filter((_, idx) => idx !== i);
    const activeTab = Math.min(fence.activeTab, tabs.length - 1);
    onUpdate({ tabs, activeTab });
  };
  const toggleRollup = () => onUpdate({ rolled: !fence.rolled });

  const onDragOver = (e) => {
    if (e.dataTransfer.types.includes('application/x-file')) {
      e.preventDefault();
      setDropHover(true);
    }
  };
  const onDragLeave = () => setDropHover(false);
  const onDrop = (e) => {
    e.preventDefault();
    setDropHover(false);
    try {
      const payload = JSON.parse(e.dataTransfer.getData('application/x-file'));
      onDropFile(payload, fence.id, activeTab.id);
      setDropped(true);
      setTimeout(() => setDropped(false), 160);
    } catch (err) {}
  };

  const height = fence.rolled ? 34 : fence.h;

  return (
    <div
      ref={wrapRef}
      className={"fence-wrap" + (fence.rolled ? " rolled" : "")}
      style={{ left: fence.x, top: fence.y, width: fence.w, height }}
      onMouseDown={onFocus}
    >
      <div
        className={"fence" + (focused ? " focused" : "") + (dropHover ? " drop-hover" : "") + (dropped ? " dropped" : "") + (fence.rolled ? " rolled" : "") + (mergeTargetId === fence.id ? " merge-target" : "")}
        onDragOver={onDragOver}
        onDragLeave={onDragLeave}
        onDrop={onDrop}
      >
        {multiTab ? (
          <div className="tabstrip" onMouseDown={startDrag}>
            {fence.tabs.map((t, i) => (
              <Tab key={t.id} tab={t} idx={i}
                active={i === fence.activeTab}
                onClick={handleTabClick}
                onClose={handleTabClose}
                onContextMenu={(e, idx) => onContextMenu(e, { kind: 'tab', fenceId: fence.id, tabIdx: idx })}
                canClose={fence.tabs.length > 1}
              />
            ))}
            <div className="tabstrip-actions">
              <div className="ts-btn" title={fence.rolled ? "展开" : "折叠"} onClick={toggleRollup}>
                {fence.rolled ? "▾" : "▴"}
              </div>
              <div className="ts-btn" title="菜单" onClick={(e) => { e.stopPropagation(); onContextMenu(e, { kind: 'fence', fenceId: fence.id }); }}>⋯</div>
            </div>
          </div>
        ) : (
          <div className="titlebar" onMouseDown={startDrag}>
            <span>{fence.title}</span>
            {fence.path && <span className="titlebar-path">— {fence.path}</span>}
            <div className="tb-actions">
              <div className="ts-btn" title={fence.rolled ? "展开" : "折叠"} onClick={toggleRollup}>
                {fence.rolled ? "▾" : "▴"}
              </div>
              <div className="ts-btn" title="菜单" onClick={(e) => { e.stopPropagation(); onContextMenu(e, { kind: 'fence', fenceId: fence.id }); }}>⋯</div>
            </div>
          </div>
        )}

        {!fence.rolled && (
          <div className="iconarea">
            {activeTab.files.length === 0 ? (
              <div className="empty-state">拖放文件到此处 · 右键可更改映射</div>
            ) : activeTab.files.map((f) => (
              <FileTile key={f} file={f} fenceId={fence.id} tabId={activeTab.id}
                selected={selected.has(f)}
                onSelect={handleSelect}
                onContextMenu={onContextMenu}
                onDragStart={onDragFile}
              />
            ))}
          </div>
        )}

        {!fence.rolled && <div className="resize-h rh-r" onMouseDown={(e) => startResize(e, 'r')} />}
        {!fence.rolled && <div className="resize-h rh-b" onMouseDown={(e) => startResize(e, 'b')} />}
        {!fence.rolled && <div className="resize-h rh-br" onMouseDown={(e) => startResize(e, 'br')} />}
      </div>
    </div>
  );
}

window.Fence = Fence;
