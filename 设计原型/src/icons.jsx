// Icon library — three parallel visual languages: fluent, tile, glass.
// Each exports the same interface: <AppIcon style="fluent" size={64} />,
// <FileIcon kind="pdf" style="tile" size={40} />, <ActionIcon name="settings" style="glass" />

// ───────── shared palette ─────────
const HUES = {
  blue:   248,
  violet: 280,
  teal:   195,
  green:  150,
  amber:   75,
  orange:  50,
  red:     25,
  pink:   350,
  slate:  240,
};

const kindHue = {
  folder:  55,   // amber manila
  doc:     220,  // word blue
  xls:     150,  // sheet green
  ppt:      25,  // ppt red-orange
  pdf:      20,  // pdf red
  img:     300,  // magenta
  video:   340,  // rose
  music:   270,  // violet
  code:    195,  // teal
  zip:      90,  // yellow-green
  exe:     248,  // app blue
  txt:     240,  // slate
  link:    210,
  ttf:     180,
};

const kindLabel = {
  folder: '', doc: 'W', xls: 'X', ppt: 'P', pdf: 'PDF',
  img: 'IMG', video: 'MP4', music: '♪', code: '<>',
  zip: 'ZIP', exe: 'EXE', txt: 'TXT', link: '↗', ttf: 'Aa',
};

const kindName = {
  folder: '项目资料', doc: '设计文档.docx', xls: '数据.xlsx',
  ppt: '汇报.pptx', pdf: '说明书.pdf', img: '截图.png',
  video: '演示.mp4', music: '音乐.mp3', code: 'app.jsx',
  zip: 'release.zip', exe: 'setup.exe', txt: '备忘.txt',
  link: '快捷方式', ttf: '字体包',
};

// ───────── APP ICON ─────────
function AppIcon({ style = 'fluent', size = 96, hue = 248 }) {
  if (style === 'fluent') return <AppFluent size={size} hue={hue} />;
  if (style === 'tile')   return <AppTile size={size} hue={hue} />;
  if (style === 'glass')  return <AppGlass size={size} hue={hue} />;
  return null;
}

function AppFluent({ size, hue }) {
  // Rounded square with a 2x2 fence grid inside. Soft inner glow, outer
  // drop shadow, diagonal highlight. The grid quadrants hint at "areas of
  // the desktop partitioned into zones".
  const r = size * 0.22;
  const pad = size * 0.18;
  const cell = (size - pad * 2 - size * 0.03) / 2;
  const gap = size * 0.03;
  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} style={{ filter: `drop-shadow(0 ${size * 0.04}px ${size * 0.06}px rgba(0,0,0,.35))` }}>
      <defs>
        <linearGradient id={`af-bg-${size}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={`oklch(68% 0.16 ${hue})`} />
          <stop offset="100%" stopColor={`oklch(42% 0.14 ${hue + 18})`} />
        </linearGradient>
        <linearGradient id={`af-hi-${size}`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="rgba(255,255,255,0.35)" />
          <stop offset="60%" stopColor="rgba(255,255,255,0)" />
        </linearGradient>
      </defs>
      <rect x="1" y="1" width={size - 2} height={size - 2} rx={r} fill={`url(#af-bg-${size})`} />
      <rect x="1" y="1" width={size - 2} height={size - 2} rx={r} fill={`url(#af-hi-${size})`} />
      {[0, 1].map(i => [0, 1].map(j => {
        const x = pad + i * (cell + gap);
        const y = pad + j * (cell + gap);
        const op = (i + j) % 2 === 0 ? 0.95 : 0.55;
        return (
          <rect key={`${i}-${j}`} x={x} y={y} width={cell} height={cell}
            rx={cell * 0.18}
            fill="white" opacity={op} />
        );
      }))}
      <rect x="1.5" y="1.5" width={size - 3} height={size - 3} rx={r - 0.5}
        fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="1" />
    </svg>
  );
}

function AppTile({ size, hue }) {
  // Four colored tiles, slightly offset, as if stacking fences. Bold,
  // Windows-11 start-menu flavor. Each tile uses a different hue but same
  // lightness/chroma for harmony.
  const r = size * 0.16;
  const cell = size * 0.4;
  const gap = size * 0.04;
  const off = (size - cell * 2 - gap) / 2;
  const tiles = [
    { hue: hue,       x: off,                y: off,                op: 1 },
    { hue: hue + 50,  x: off + cell + gap,   y: off,                op: 0.88 },
    { hue: hue - 50,  x: off,                y: off + cell + gap,   op: 0.88 },
    { hue: hue + 100, x: off + cell + gap,   y: off + cell + gap,   op: 1 },
  ];
  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} style={{ filter: `drop-shadow(0 ${size * 0.04}px ${size * 0.07}px rgba(0,0,0,.38))` }}>
      {tiles.map((t, i) => (
        <g key={i}>
          <rect x={t.x} y={t.y} width={cell} height={cell} rx={r}
            fill={`oklch(62% 0.16 ${t.hue})`} opacity={t.op} />
          <rect x={t.x} y={t.y} width={cell} height={cell * 0.45} rx={r}
            fill="white" opacity="0.13" />
        </g>
      ))}
    </svg>
  );
}

function AppGlass({ size, hue }) {
  // Translucent glass panel with a subtle fence grid — 1.5px strokes,
  // saturated edge-tint, very "acrylic material" flavor.
  const r = size * 0.22;
  const pad = size * 0.22;
  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
      <defs>
        <linearGradient id={`ag-bg-${size}`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor={`oklch(55% 0.14 ${hue} / 0.9)`} />
          <stop offset="100%" stopColor={`oklch(35% 0.12 ${hue + 30} / 0.9)`} />
        </linearGradient>
        <linearGradient id={`ag-edge-${size}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="rgba(255,255,255,0.6)" />
          <stop offset="50%" stopColor="rgba(255,255,255,0.1)" />
          <stop offset="100%" stopColor="rgba(255,255,255,0.3)" />
        </linearGradient>
      </defs>
      <rect x="2" y="2" width={size - 4} height={size - 4} rx={r}
        fill={`url(#ag-bg-${size})`} />
      <rect x="2" y="2" width={size - 4} height={size - 4} rx={r}
        fill="none" stroke={`url(#ag-edge-${size})`} strokeWidth={size * 0.02} />
      {/* inner grid — 1.5px cross */}
      <line x1={size / 2} y1={pad} x2={size / 2} y2={size - pad}
        stroke="rgba(255,255,255,0.85)" strokeWidth={size * 0.02} strokeLinecap="round" />
      <line x1={pad} y1={size / 2} x2={size - pad} y2={size / 2}
        stroke="rgba(255,255,255,0.85)" strokeWidth={size * 0.02} strokeLinecap="round" />
      {/* glass highlight arc */}
      <path d={`M ${pad * 0.8} ${size * 0.25} Q ${size * 0.35} ${pad * 0.5} ${size * 0.65} ${pad * 0.7}`}
        fill="none" stroke="rgba(255,255,255,0.5)" strokeWidth={size * 0.015} strokeLinecap="round" />
    </svg>
  );
}

// ───────── FILE ICON ─────────
function FileIcon({ kind = 'doc', style = 'fluent', size = 48 }) {
  const hue = kindHue[kind] ?? 240;
  if (style === 'fluent') return <FileFluent kind={kind} size={size} hue={hue} />;
  if (style === 'tile')   return <FileTile kind={kind} size={size} hue={hue} />;
  if (style === 'glass')  return <FileGlass kind={kind} size={size} hue={hue} />;
  return null;
}

function FileFluent({ kind, size, hue }) {
  // Document metaphor: rectangle with folded corner for file types,
  // manila-folder shape for folders.
  if (kind === 'folder') {
    return (
      <svg width={size} height={size} viewBox="0 0 48 48" style={{ filter: 'drop-shadow(0 2px 3px rgba(0,0,0,.3))' }}>
        <defs>
          <linearGradient id={`ff-fld-${size}`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={`oklch(72% 0.14 ${hue})`} />
            <stop offset="100%" stopColor={`oklch(52% 0.15 ${hue - 8})`} />
          </linearGradient>
        </defs>
        <path d="M 4 14 Q 4 10 8 10 L 18 10 L 22 14 L 40 14 Q 44 14 44 18 L 44 36 Q 44 40 40 40 L 8 40 Q 4 40 4 36 Z"
          fill={`url(#ff-fld-${size})`} />
        <path d="M 4 16 L 44 16" stroke="rgba(255,255,255,0.4)" strokeWidth="1.2" />
      </svg>
    );
  }
  // document
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" style={{ filter: 'drop-shadow(0 2px 3px rgba(0,0,0,.28))' }}>
      <defs>
        <linearGradient id={`ff-doc-${size}-${kind}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#ffffff" />
          <stop offset="100%" stopColor="#e8ecf4" />
        </linearGradient>
        <linearGradient id={`ff-tab-${size}-${kind}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={`oklch(62% 0.17 ${hue})`} />
          <stop offset="100%" stopColor={`oklch(48% 0.17 ${hue + 10})`} />
        </linearGradient>
      </defs>
      {/* page */}
      <path d="M 10 5 L 32 5 L 42 15 L 42 43 Q 42 45 40 45 L 10 45 Q 8 45 8 43 L 8 7 Q 8 5 10 5 Z"
        fill={`url(#ff-doc-${size}-${kind})`} stroke="rgba(40,50,70,0.18)" strokeWidth="0.8" />
      {/* fold */}
      <path d="M 32 5 L 42 15 L 34 15 Q 32 15 32 13 Z"
        fill="rgba(40,50,70,0.12)" />
      {/* colored bottom tab w/ file-type letters */}
      <rect x="8" y="28" width="28" height="12" rx="2" fill={`url(#ff-tab-${size}-${kind})`} />
      <text x="22" y="37" textAnchor="middle" fill="white"
        fontSize={kindLabel[kind].length > 2 ? 7 : 8.5}
        fontWeight="700" fontFamily="Segoe UI, system-ui" letterSpacing="0.5">
        {kindLabel[kind]}
      </text>
    </svg>
  );
}

function FileTile({ kind, size, hue }) {
  // Bold rounded squircle tile with glyph. Very app-icon flavor.
  if (kind === 'folder') {
    return (
      <svg width={size} height={size} viewBox="0 0 48 48" style={{ filter: 'drop-shadow(0 3px 5px rgba(0,0,0,.32))' }}>
        <defs>
          <linearGradient id={`ft-fld-${size}`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={`oklch(75% 0.15 ${hue})`} />
            <stop offset="100%" stopColor={`oklch(50% 0.17 ${hue - 10})`} />
          </linearGradient>
        </defs>
        <rect x="3" y="3" width="42" height="42" rx="10" fill={`url(#ft-fld-${size})`} />
        <rect x="3" y="3" width="42" height="16" rx="10" fill="white" opacity="0.14" />
        {/* mini folder glyph */}
        <path d="M 14 18 L 20 18 L 22 20 L 34 20 Q 35 20 35 21 L 35 32 Q 35 33 34 33 L 14 33 Q 13 33 13 32 L 13 19 Q 13 18 14 18 Z"
          fill="white" opacity="0.92" />
      </svg>
    );
  }
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" style={{ filter: 'drop-shadow(0 3px 5px rgba(0,0,0,.3))' }}>
      <defs>
        <linearGradient id={`ft-bg-${size}-${kind}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={`oklch(68% 0.17 ${hue})`} />
          <stop offset="100%" stopColor={`oklch(45% 0.17 ${hue + 14})`} />
        </linearGradient>
      </defs>
      <rect x="3" y="3" width="42" height="42" rx="10" fill={`url(#ft-bg-${size}-${kind})`} />
      <rect x="3" y="3" width="42" height="18" rx="10" fill="white" opacity="0.13" />
      <text x="24" y="30" textAnchor="middle" fill="white"
        fontSize={kindLabel[kind].length > 2 ? 12 : 16}
        fontWeight="700" fontFamily="Segoe UI, system-ui" letterSpacing="0.3">
        {kindLabel[kind] || '•'}
      </text>
    </svg>
  );
}

function FileGlass({ kind, size, hue }) {
  // Line-art on translucent tinted backdrop. Editor/pro flavor.
  if (kind === 'folder') {
    return (
      <svg width={size} height={size} viewBox="0 0 48 48">
        <rect x="3" y="3" width="42" height="42" rx="10"
          fill={`oklch(55% 0.14 ${hue} / 0.22)`} stroke={`oklch(72% 0.14 ${hue} / 0.55)`} strokeWidth="1" />
        <path d="M 12 18 L 20 18 L 22.5 20.5 L 36 20.5 Q 37 20.5 37 21.5 L 37 32 Q 37 33 36 33 L 12 33 Q 11 33 11 32 L 11 19 Q 11 18 12 18 Z"
          fill="none" stroke={`oklch(82% 0.14 ${hue})`} strokeWidth="1.6" strokeLinejoin="round" />
      </svg>
    );
  }
  return (
    <svg width={size} height={size} viewBox="0 0 48 48">
      <rect x="3" y="3" width="42" height="42" rx="10"
        fill={`oklch(55% 0.14 ${hue} / 0.22)`} stroke={`oklch(72% 0.14 ${hue} / 0.55)`} strokeWidth="1" />
      <path d="M 16 11 L 30 11 L 35 16 L 35 36 Q 35 37 34 37 L 16 37 Q 15 37 15 36 L 15 12 Q 15 11 16 11 Z"
        fill="none" stroke={`oklch(82% 0.14 ${hue})`} strokeWidth="1.6" strokeLinejoin="round" />
      <path d="M 30 11 L 30 16 L 35 16" fill="none" stroke={`oklch(82% 0.14 ${hue})`} strokeWidth="1.4" strokeLinejoin="round" />
      <text x="25" y="31" textAnchor="middle" fill={`oklch(90% 0.08 ${hue})`}
        fontSize={kindLabel[kind].length > 2 ? 6.5 : 7.5}
        fontWeight="600" fontFamily="JetBrains Mono, monospace" letterSpacing="0.5">
        {kindLabel[kind]}
      </text>
    </svg>
  );
}

// ───────── ACTION ICON ─────────
// UI actions: settings, search, pin, hide, rollup, peek, add-fence,
// lock, merge, split, delete
function ActionIcon({ name, style = 'fluent', size = 24, color }) {
  const base = { width: size, height: size };
  const c = color || (style === 'glass' ? '#cfd6e6' : style === 'tile' ? '#ffffff' : '#e8ecf4');

  // shared path defs
  const paths = {
    settings: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="2.8" />
      <path d="M12 2v3M12 19v3M4.2 4.2l2.1 2.1M17.7 17.7l2.1 2.1M2 12h3M19 12h3M4.2 19.8l2.1-2.1M17.7 6.3l2.1-2.1" />
    </g>,
    search: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round">
      <circle cx="11" cy="11" r="6.5" /><path d="m20 20-3.8-3.8" />
    </g>,
    pin: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinejoin="round" strokeLinecap="round">
      <path d="M14 3l7 7-4 1-4 4-1 5-4-4-5 5v-1l5-5-4-4 5-1 4-4z" />
    </g>,
    hide: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M3 3l18 18" /><path d="M10.6 6.2A10 10 0 0 1 21 12c-.6 1.2-1.4 2.3-2.3 3.2M6.1 6.1C4.3 7.5 2.8 9.5 2 12a10 10 0 0 0 13.9 5.9" />
      <circle cx="12" cy="12" r="3" />
    </g>,
    rollup: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M6 9l6-6 6 6" /><path d="M4 15h16M4 20h16" />
    </g>,
    peek: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="3" width="18" height="18" rx="3" />
      <path d="M8 10h8M8 14h5" />
      <circle cx="18" cy="18" r="1.3" fill={c} stroke="none" />
    </g>,
    add: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round">
      <rect x="3" y="3" width="18" height="18" rx="3" />
      <path d="M12 8v8M8 12h8" />
    </g>,
    lock: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <rect x="5" y="11" width="14" height="10" rx="2" /><path d="M8 11V8a4 4 0 0 1 8 0v3" />
    </g>,
    merge: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="5" width="7" height="14" rx="1.5" /><rect x="14" y="5" width="7" height="14" rx="1.5" />
      <path d="M10.5 12h3M12 10.5l1.5 1.5-1.5 1.5" />
    </g>,
    split: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="5" width="18" height="14" rx="2" /><path d="M12 5v14" />
    </g>,
    trash: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 7h16M9 7V4h6v3M6 7l1 13a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-13" />
      <path d="M10 11v7M14 11v7" />
    </g>,
    rule: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 6h10M4 12h16M4 18h7" /><circle cx="18" cy="6" r="2" /><circle cx="14" cy="18" r="2" />
    </g>,
    portal: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <ellipse cx="12" cy="12" rx="4" ry="9" /><ellipse cx="12" cy="12" rx="9" ry="4" />
    </g>,
    theme: <g fill="none" stroke={c} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 3a9 9 0 0 0 0 18 4 4 0 0 0 0-8 4 4 0 0 1 0-8z" />
    </g>,
  };

  if (style === 'tile') {
    return (
      <svg {...base} viewBox="0 0 24 24">
        <rect x="1" y="1" width="22" height="22" rx="5"
          fill="oklch(58% 0.14 248)" />
        <rect x="1" y="1" width="22" height="11" rx="5" fill="white" opacity="0.14" />
        {paths[name]}
      </svg>
    );
  }
  if (style === 'glass') {
    return (
      <svg {...base} viewBox="0 0 24 24">
        <rect x="1" y="1" width="22" height="22" rx="6"
          fill="rgba(255,255,255,0.04)" stroke="rgba(255,255,255,0.12)" strokeWidth="0.8" />
        {paths[name]}
      </svg>
    );
  }
  // fluent — naked glyph
  return <svg {...base} viewBox="0 0 24 24">{paths[name]}</svg>;
}

// Expose
Object.assign(window, { AppIcon, FileIcon, ActionIcon, HUES, kindHue, kindLabel, kindName });
