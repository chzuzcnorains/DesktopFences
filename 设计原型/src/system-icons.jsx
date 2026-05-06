// System (Windows-style) original file icons.
// Classic page-with-fold + colored badge motif. Rendered at any size.
// Exposes window.SystemIcon({ kind, label, size }).

function SystemIcon({ kind, label, size = 40 }) {
  const s = size;
  // Folder is the only non-page shape
  if (kind === 'folder') {
    return (
      <svg width={s} height={s} viewBox="0 0 48 48" style={{ display: 'block' }}>
        <defs>
          <linearGradient id={`sf-${s}`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#ffd66b" />
            <stop offset="100%" stopColor="#e8a93a" />
          </linearGradient>
          <linearGradient id={`sf2-${s}`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#ffe28a" />
            <stop offset="100%" stopColor="#f0bb52" />
          </linearGradient>
        </defs>
        <path d="M4 12 L4 38 Q4 41 7 41 L41 41 Q44 41 44 38 L44 17 Q44 14 41 14 L22 14 L18 10 L7 10 Q4 10 4 13 Z"
              fill={`url(#sf-${s})`} stroke="#a47620" strokeWidth="0.6" />
        <path d="M6 18 L42 18 L42 38 Q42 39.5 40.5 39.5 L7.5 39.5 Q6 39.5 6 38 Z"
              fill={`url(#sf2-${s})`} opacity="0.9" />
      </svg>
    );
  }

  // Page with folded corner — base for all file types
  // Color theme by kind
  const themes = {
    doc:  { badge: '#2b5cae', text: 'W',   accent: '#4b7fd4' },
    xls:  { badge: '#1e7d4a', text: 'X',   accent: '#3aa76b' },
    ppt:  { badge: '#c43e1c', text: 'P',   accent: '#e0653f' },
    pdf:  { badge: '#c02535', text: 'PDF', accent: '#e04a5a' },
    img:  { badge: '#7b4fb5', text: '',    accent: '#9a6ed0' },
    code: { badge: '#3a6f8e', text: '<>',  accent: '#5a99bb' },
    sql:  { badge: '#2f7d7d', text: 'SQL', accent: '#4aa8a8' },
    ps1:  { badge: '#1e3a7a', text: '>_',  accent: '#3455a8' },
    txt:  { badge: '#5a6478', text: 'TXT', accent: '#7b8294' },
    md:   { badge: '#2f4858', text: 'MD',  accent: '#4a6878' },
    exe:  { badge: '#4d4d4d', text: '',    accent: '#707070' },
    zip:  { badge: '#7a6638', text: 'ZIP', accent: '#a48650' },
    video:{ badge: '#a02e5a', text: '',    accent: '#c85581' },
  };
  const t = themes[kind] || themes.txt;
  const id = `${kind}-${s}`;

  // EXE / shortcut: monitor-like glyph instead of page
  if (kind === 'exe') {
    return (
      <svg width={s} height={s} viewBox="0 0 48 48" style={{ display: 'block' }}>
        <defs>
          <linearGradient id={`se-${id}`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#dfe4ee" />
            <stop offset="100%" stopColor="#a8b1c2" />
          </linearGradient>
        </defs>
        <rect x="6" y="9" width="36" height="24" rx="2" fill={`url(#se-${id})`} stroke="#5a6378" strokeWidth="0.6" />
        <rect x="9" y="12" width="30" height="18" rx="1" fill="#1c2334" />
        <path d="M14 18 L20 22 L14 26 Z" fill="#3a9bff" />
        <path d="M22 24 L28 24" stroke="#3a9bff" strokeWidth="1.4" strokeLinecap="round" />
        <path d="M18 36 L30 36 L33 41 L15 41 Z" fill="#bcc4d4" stroke="#5a6378" strokeWidth="0.6" />
      </svg>
    );
  }

  // Image kind: photo thumbnail with mountain
  if (kind === 'img') {
    return (
      <svg width={s} height={s} viewBox="0 0 48 48" style={{ display: 'block' }}>
        <defs>
          <linearGradient id={`si-${id}`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#b8a3d8" />
            <stop offset="100%" stopColor="#7b4fb5" />
          </linearGradient>
        </defs>
        <rect x="6" y="7" width="36" height="34" rx="2" fill="#f5f3fa" stroke="#7b4fb5" strokeWidth="0.7" />
        <rect x="9" y="10" width="30" height="28" rx="1" fill={`url(#si-${id})`} />
        <circle cx="16" cy="17" r="2.4" fill="#fff3b0" />
        <path d="M9 32 L18 22 L25 28 L31 23 L39 32 L39 38 L9 38 Z" fill="#3a3052" opacity="0.85" />
      </svg>
    );
  }

  // Video kind: filmstrip + play
  if (kind === 'video') {
    return (
      <svg width={s} height={s} viewBox="0 0 48 48" style={{ display: 'block' }}>
        <rect x="6" y="9" width="36" height="30" rx="3" fill="#1d1322" stroke="#a02e5a" strokeWidth="0.7" />
        <g fill="#c85581">
          <rect x="9" y="12" width="3" height="3" /><rect x="9" y="18" width="3" height="3" />
          <rect x="9" y="24" width="3" height="3" /><rect x="9" y="30" width="3" height="3" />
          <rect x="36" y="12" width="3" height="3" /><rect x="36" y="18" width="3" height="3" />
          <rect x="36" y="24" width="3" height="3" /><rect x="36" y="30" width="3" height="3" />
        </g>
        <path d="M21 18 L31 24 L21 30 Z" fill="#fff" />
      </svg>
    );
  }

  // Default: page with folded corner + colored bottom badge
  return (
    <svg width={s} height={s} viewBox="0 0 48 48" style={{ display: 'block' }}>
      <defs>
        <linearGradient id={`sp-${id}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#ffffff" />
          <stop offset="100%" stopColor="#e3e7ee" />
        </linearGradient>
      </defs>
      {/* page body with folded corner */}
      <path d="M9 5 L31 5 L41 15 L41 41 Q41 43 39 43 L9 43 Q7 43 7 41 L7 7 Q7 5 9 5 Z"
            fill={`url(#sp-${id})`} stroke="#9aa3b5" strokeWidth="0.7" />
      {/* fold */}
      <path d="M31 5 L31 13 Q31 15 33 15 L41 15 Z" fill="#cdd3df" stroke="#9aa3b5" strokeWidth="0.5" />
      {/* faint text lines */}
      <g stroke="#c8cdd8" strokeWidth="0.8" strokeLinecap="round" opacity="0.7">
        <line x1="12" y1="20" x2="26" y2="20" />
        <line x1="12" y1="23.5" x2="30" y2="23.5" />
      </g>
      {/* colored bottom badge */}
      <path d="M7 30 Q7 28 9 28 L33 28 Q35 28 35 30 L35 41 Q35 43 33 43 L9 43 Q7 43 7 41 Z"
            fill={t.badge} />
      <text x="21" y="39.5" textAnchor="middle"
            fontFamily="Segoe UI, Arial, sans-serif"
            fontSize={t.text.length >= 3 ? 8 : t.text.length === 2 ? 10 : 12}
            fontWeight="700" fill="#fff" letterSpacing="0.3">
        {t.text || (label || '').slice(0, 3).toUpperCase()}
      </text>
    </svg>
  );
}

// Plain-DOM version for the native-icon column (which is not React-driven).
function systemIconHTML(kind, label, size = 40) {
  // Reuse SystemIcon by rendering via ReactDOMServer? Not loaded.
  // Build minimal SVG markup mirroring the React component above.
  if (kind === 'folder') {
    return `<svg width="${size}" height="${size}" viewBox="0 0 48 48" style="display:block">
      <defs>
        <linearGradient id="nsf" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#ffd66b"/><stop offset="100%" stop-color="#e8a93a"/>
        </linearGradient>
        <linearGradient id="nsf2" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#ffe28a"/><stop offset="100%" stop-color="#f0bb52"/>
        </linearGradient>
      </defs>
      <path d="M4 12 L4 38 Q4 41 7 41 L41 41 Q44 41 44 38 L44 17 Q44 14 41 14 L22 14 L18 10 L7 10 Q4 10 4 13 Z" fill="url(#nsf)" stroke="#a47620" stroke-width="0.6"/>
      <path d="M6 18 L42 18 L42 38 Q42 39.5 40.5 39.5 L7.5 39.5 Q6 39.5 6 38 Z" fill="url(#nsf2)" opacity="0.9"/>
    </svg>`;
  }
  if (kind === 'exe') {
    return `<svg width="${size}" height="${size}" viewBox="0 0 48 48" style="display:block">
      <rect x="6" y="9" width="36" height="24" rx="2" fill="#cdd3df" stroke="#5a6378" stroke-width="0.6"/>
      <rect x="9" y="12" width="30" height="18" rx="1" fill="#1c2334"/>
      <path d="M14 18 L20 22 L14 26 Z" fill="#3a9bff"/>
      <path d="M22 24 L28 24" stroke="#3a9bff" stroke-width="1.4" stroke-linecap="round"/>
      <path d="M18 36 L30 36 L33 41 L15 41 Z" fill="#bcc4d4" stroke="#5a6378" stroke-width="0.6"/>
    </svg>`;
  }
  // recycle bin / 此电脑 etc come through as "exe" / "img" in NATIVE_ICONS,
  // but for the desktop-side we'll fall back to label rendering for these sentinel labels:
  return `<svg width="${size}" height="${size}" viewBox="0 0 48 48" style="display:block">
    <path d="M9 5 L31 5 L41 15 L41 41 Q41 43 39 43 L9 43 Q7 43 7 41 L7 7 Q7 5 9 5 Z" fill="#f0f3f9" stroke="#9aa3b5" stroke-width="0.7"/>
    <path d="M31 5 L31 13 Q31 15 33 15 L41 15 Z" fill="#cdd3df" stroke="#9aa3b5" stroke-width="0.5"/>
  </svg>`;
}

window.SystemIcon = SystemIcon;
window.systemIconHTML = systemIconHTML;
