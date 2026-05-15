/* global React */
// chrome.jsx — Tessera browser chrome (tabs, URL bar, status, webview)
// Two chrome variations share the same atoms (URL bar, build pill, page
// content) but compose them differently.

const { useState, useMemo } = React;

/* ─── Icons ──────────────────────────────────────────────────────────
   Hand-drawn minimal strokes. 16×16 grid, 1.5px stroke. Matches Arc-soft
   feel. */
const Icon = ({ d, size = 16, fill = 'none', strokeWidth = 1.5, style }) => (
  <svg width={size} height={size} viewBox="0 0 16 16" fill="none"
       style={{ flex: '0 0 auto', display: 'block', ...style }}>
    <path d={d} stroke="currentColor" strokeWidth={strokeWidth}
          strokeLinecap="round" strokeLinejoin="round" fill={fill} />
  </svg>
);

const ICONS = {
  back:    'M9.5 3.5 5 8l4.5 4.5',
  fwd:     'M6.5 3.5 11 8l-4.5 4.5',
  reload:  'M13 8a5 5 0 1 1-1.5-3.5M13 3v2h-2',
  go:      'M3 8h10M9 4l4 4-4 4',
  find:    'M7 12a5 5 0 1 0 0-10 5 5 0 0 0 0 10ZM14 14l-3.2-3.2',
  enter:   'M13 4v3a2 2 0 0 1-2 2H3M6 12 3 9l3-3',
  add:     'M8 3v10M3 8h10',
  close:   'M3.5 3.5l9 9M12.5 3.5l-9 9',
  lock:    'M4.5 7V5.5a3.5 3.5 0 0 1 7 0V7M3.5 7h9v6h-9z',
  shield:  'M8 1.5 13 3v5c0 3-2.5 5.5-5 6.5C5.5 13.5 3 11 3 8V3l5-1.5Z',
  inspect: 'M3 3v6l2.5-1L7 11l1.5-.5L7 7l3-1Z',
  console: 'M2 4h12v8H2zM4.5 7l2 1.5-2 1.5M8 10h3.5',
  bug:     'M5 5.5V4a3 3 0 0 1 6 0v1.5M5 5.5h6v4a3 3 0 0 1-6 0v-4ZM3 7h2M11 7h2M3 11h2M11 11h2M8 5.5v8',
  spark:   'M2 12 5 7l2.5 3L11 4l3 8',
  cpu:     'M4 4h8v8H4zM6 6h4v4H6zM6 2v2M10 2v2M6 12v2M10 12v2M2 6h2M2 10h2M12 6h2M12 10h2',
  layers:  'M8 2 2 5l6 3 6-3-6-3ZM2 8l6 3 6-3M2 11l6 3 6-3',
  star:    'M8 2l1.8 3.7 4 .6-2.9 2.9.7 4L8 11.3l-3.6 1.9.7-4L2.2 6.3l4-.6L8 2Z',
  panelB:  'M2 3h12v10H2zM2 9h12',
  panelR:  'M2 3h12v10H2zM10 3v10',
  detach:  'M3 3h6v6H3zM7 7h6v6H7z',
  more:    'M4 8a.7.7 0 1 1-1.4 0 .7.7 0 0 1 1.4 0ZM8.7 8a.7.7 0 1 1-1.4 0 .7.7 0 0 1 1.4 0ZM13.4 8a.7.7 0 1 1-1.4 0 .7.7 0 0 1 1.4 0Z',
  triRight:'M6 4l4 4-4 4',
  triDown: 'M4 6l4 4 4-4',
  rec:     'M8 4a4 4 0 1 0 0 8 4 4 0 0 0 0-8Z',
  cmd:     'M5 5a1.5 1.5 0 1 0 1.5 1.5V5H5ZM11 5a1.5 1.5 0 1 1-1.5 1.5V5H11ZM5 11a1.5 1.5 0 1 1 1.5-1.5V11H5ZM11 11a1.5 1.5 0 1 0-1.5-1.5V11H11ZM6.5 6.5h3v3h-3z',
};

const IconBtn = ({ name, label, on, ...rest }) => (
  <button title={label} aria-label={label} {...rest} style={{
    width: 'var(--row)', height: 'var(--row)',
    display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
    borderRadius: 'var(--r-md)',
    color: on ? 'var(--accent)' : 'var(--text-2)',
    background: on ? 'var(--accent-bg)' : 'transparent',
    transition: 'background .12s, color .12s',
    ...rest.style,
  }}>
    <Icon d={ICONS[name]} />
  </button>
);

/* ─── Build pill ─────────────────────────────────────────────────── */
function BuildPill({ milestone = 'M3', flags = [] }) {
  return (
    <span className="pill">
      <span className="dot" />
      <b style={{ fontWeight: 600 }}>{milestone}</b>
      <span style={{ opacity: 0.6 }}>·</span>
      {flags.map((f, i) => (
        <React.Fragment key={f}>
          {i > 0 && <span style={{ opacity: 0.5 }}>·</span>}
          <span>{f}</span>
        </React.Fragment>
      ))}
    </span>
  );
}

/* ─── Mini load chart — the per-request waterfall that lives INSIDE
       the URL bar during load. Compact, ultra-information-dense. */
function MiniLoadChart({ phases, totalMs }) {
  // phases: [{label, t, dur, cat}]
  const max = totalMs;
  return (
    <div style={{
      display: 'inline-flex', alignItems: 'center', gap: 6,
      padding: '0 8px', height: 22,
      borderRadius: 'var(--r-sm)',
      background: 'var(--surface)',
      border: '1px solid var(--border)',
      fontFamily: 'var(--font-mono)',
      fontSize: 10, color: 'var(--muted)',
    }}>
      <span style={{ color: 'var(--accent)' }}>●</span>
      <span style={{ width: 140, height: 8, position: 'relative' }}>
        {phases.map((p, i) => (
          <span key={i} title={`${p.label} · ${p.dur}ms`} style={{
            position: 'absolute',
            left:  `${(p.t / max) * 100}%`,
            width: `${(p.dur / max) * 100}%`,
            top: 0, height: 8,
            background: `var(--cat-${p.cat})`,
            borderRadius: 2,
            opacity: 0.92,
          }} />
        ))}
        {/* live cursor */}
        <span style={{
          position: 'absolute', left: '78%', top: -2, bottom: -2,
          width: 1, background: 'var(--text)', opacity: 0.7,
        }} />
      </span>
      <span style={{ minWidth: 38, textAlign: 'right' }}>{totalMs}ms</span>
    </div>
  );
}

/* ─── URL bar ────────────────────────────────────────────────────── */
function UrlBar({ url, secure = true, loading = false, phases, totalMs, variant = 'a' }) {
  return (
    <div style={{
      display: 'flex', alignItems: 'center', gap: 'var(--gap-sm)',
      height: 'var(--row)', padding: '0 8px 0 10px',
      background: 'var(--surface)',
      border: '1px solid var(--border)',
      borderRadius: 'var(--r-md)',
      flex: 1, minWidth: 0,
    }}>
      <Icon d={secure ? ICONS.lock : ICONS.shield} size={14}
            style={{ color: secure ? 'var(--ok)' : 'var(--muted)' }} />
      <span style={{
        fontFamily: 'var(--font-mono)',
        fontSize: 'var(--fs-sm)',
        color: 'var(--text)',
        flex: 1, minWidth: 0,
        whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
      }}>
        <span style={{ color: 'var(--muted)' }}>{url.scheme}://</span>
        <span>{url.host}</span>
        <span style={{ color: 'var(--muted)' }}>{url.path}</span>
      </span>
      {loading && <MiniLoadChart phases={phases} totalMs={totalMs} />}
      <button style={{
        padding: '0 8px', height: 22,
        display: 'inline-flex', alignItems: 'center', gap: 4,
        color: 'var(--muted)', fontSize: 'var(--fs-xs)',
        fontFamily: 'var(--font-mono)',
      }}>
        <Icon d={ICONS.find} size={12} />
        find
      </button>
    </div>
  );
}

/* ─── Tab atoms ──────────────────────────────────────────────────── */
function Favicon({ host, size = 14 }) {
  // letterform pulled from the host's first character
  const ch = (host || '?').replace(/^www\./, '').charAt(0).toUpperCase();
  // deterministic hue per host
  let h = 0;
  for (let i = 0; i < (host || '').length; i++) h = (h * 31 + host.charCodeAt(i)) % 360;
  return (
    <span style={{
      width: size, height: size,
      display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
      borderRadius: 4,
      background: `oklch(0.65 0.13 ${h})`,
      color: '#fff',
      fontFamily: 'var(--font-mono)',
      fontSize: size * 0.62,
      fontWeight: 600,
      flex: '0 0 auto',
    }}>{ch}</span>
  );
}

/* ─── Horizontal tab strip (Variation A) ─────────────────────────── */
function TabStripA({ tabs, activeId }) {
  return (
    <div style={{
      display: 'flex', alignItems: 'flex-end', gap: 2,
      padding: '0 var(--pad-sm)',
      height: 'calc(var(--row) + 2px)',
      overflow: 'hidden',
    }}>
      {tabs.map(t => {
        const active = t.id === activeId;
        return (
          <div key={t.id} style={{
            display: 'flex', alignItems: 'center', gap: 6,
            padding: '0 10px',
            height: 'var(--row)',
            minWidth: 120, maxWidth: 220,
            borderRadius: 'var(--r-md) var(--r-md) 0 0',
            background: active ? 'var(--panel)' : 'transparent',
            color: active ? 'var(--text)' : 'var(--muted)',
            position: 'relative',
            cursor: 'pointer',
          }}>
            {t.loading
              ? <span style={{
                  width: 12, height: 12,
                  borderRadius: '50%',
                  border: '1.5px solid var(--accent)',
                  borderTopColor: 'transparent',
                  flex: '0 0 auto',
                }} />
              : <Favicon host={t.host} size={12} />
            }
            <span style={{
              flex: 1, minWidth: 0,
              fontSize: 'var(--fs-sm)',
              whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
            }}>{t.title}</span>
            <Icon d={ICONS.close} size={10}
                  style={{ color: 'var(--muted)', opacity: active ? 1 : 0.5 }} />
            {active && <span style={{
              position: 'absolute', left: 8, right: 8, bottom: -1,
              height: 1, background: 'var(--panel)',
            }} />}
          </div>
        );
      })}
      <button style={{
        width: 'var(--row)', height: 'var(--row)',
        display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
        color: 'var(--muted)',
      }}>
        <Icon d={ICONS.add} size={12} />
      </button>
    </div>
  );
}

/* ─── Vertical tab sidebar (Variation B) ─────────────────────────── */
function TabStripB({ tabs, activeId, pinned = [] }) {
  const Section = ({ title, items }) => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
      {title && <div style={{
        padding: '8px 10px 4px',
        fontSize: 10, color: 'var(--faint)',
        fontFamily: 'var(--font-mono)',
        letterSpacing: '0.06em', textTransform: 'uppercase',
      }}>{title}</div>}
      {items.map(t => {
        const active = t.id === activeId;
        return (
          <div key={t.id} style={{
            display: 'flex', alignItems: 'center', gap: 8,
            padding: '0 10px',
            height: 'var(--row-sm)',
            margin: '0 6px',
            borderRadius: 'var(--r-sm)',
            background: active ? 'var(--surface)' : 'transparent',
            color: active ? 'var(--text)' : 'var(--text-2)',
            position: 'relative',
            cursor: 'pointer',
          }}>
            {active && <span style={{
              position: 'absolute', left: -6, top: 6, bottom: 6, width: 2,
              background: 'var(--accent)', borderRadius: 2,
            }} />}
            {t.loading
              ? <span style={{
                  width: 12, height: 12,
                  borderRadius: '50%',
                  border: '1.5px solid var(--accent)',
                  borderTopColor: 'transparent',
                  flex: '0 0 auto',
                }} />
              : <Favicon host={t.host} size={12} />
            }
            <span style={{
              flex: 1, minWidth: 0,
              fontSize: 'var(--fs-sm)',
              whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
            }}>{t.title}</span>
            {t.audio && <span style={{
              width: 6, height: 6, borderRadius: 3,
              background: 'var(--accent)',
            }} />}
          </div>
        );
      })}
    </div>
  );
  return (
    <div style={{
      width: 220, flex: '0 0 220px',
      display: 'flex', flexDirection: 'column',
      borderRight: '1px solid var(--border)',
      background: 'var(--bg)',
    }}>
      <div style={{
        height: 38, padding: '0 14px',
        display: 'flex', alignItems: 'center', gap: 8,
        WebkitAppRegion: 'drag',
      }}>
        <span style={{
          fontFamily: 'var(--font-mono)',
          fontSize: 'var(--fs-md)',
          fontWeight: 600,
          letterSpacing: '-0.01em',
        }}>tessera</span>
      </div>
      <div style={{
        margin: '0 8px 8px',
        height: 28, padding: '0 8px',
        display: 'flex', alignItems: 'center', gap: 6,
        borderRadius: 'var(--r-sm)',
        background: 'var(--surface)',
        border: '1px solid var(--border)',
        color: 'var(--muted)',
        fontSize: 'var(--fs-xs)',
        fontFamily: 'var(--font-mono)',
      }}>
        <Icon d={ICONS.cmd} size={11} />
        <span style={{ flex: 1 }}>search · jump · run</span>
        <span style={{ opacity: 0.6 }}>⌘K</span>
      </div>
      <div style={{ flex: 1, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
        <Section title="Pinned" items={pinned} />
        <Section title="Today" items={tabs} />
      </div>
      <div style={{
        padding: 'var(--pad-sm)',
        borderTop: '1px solid var(--border)',
        display: 'flex', alignItems: 'center', gap: 8,
      }}>
        <BuildPill milestone="M3" flags={['flow layout', 'async loader']} />
      </div>
    </div>
  );
}

/* ─── Webview (fake-rendered page) ───────────────────────────────── */
function Webview({ state = 'rendered' }) {
  if (state === 'loading') {
    return (
      <div style={{
        flex: 1, background: 'var(--web-bg)',
        display: 'flex', flexDirection: 'column',
        position: 'relative', overflow: 'hidden',
      }}>
        {/* Skeleton: parser has produced some text but layout hasn't run */}
        <div style={{
          padding: '40px 60px',
          color: 'var(--web-text)',
          fontFamily: 'Georgia, serif',
          fontSize: 14, lineHeight: 1.6, opacity: 0.4,
        }}>
          <span style={{ color: '#999' }}>justinjackson.ca/words.html</span>
        </div>
        <div style={{
          position: 'absolute', left: 60, right: 60, top: 100,
          display: 'flex', flexDirection: 'column', gap: 8,
        }}>
          {[100,80,90,70,60].map((w, i) => (
            <div key={i} style={{
              height: 14, width: `${w}%`,
              background: 'linear-gradient(90deg, #eee 0%, #f4f4f4 50%, #eee 100%)',
              backgroundSize: '200% 100%',
              animation: 'shimmer 1.8s linear infinite',
              borderRadius: 2,
            }} />
          ))}
        </div>
      </div>
    );
  }

  // "Rendered" state — a stylized rendering of justinjackson.ca/words.html
  return (
    <div style={{
      flex: 1, background: 'var(--web-bg)',
      overflow: 'hidden', position: 'relative',
      fontFamily: 'Georgia, "Times New Roman", serif',
      color: 'var(--web-text)',
    }}>
      <div style={{
        maxWidth: 640, margin: '0 auto', padding: '60px 32px',
      }}>
        <h1 style={{
          fontSize: 64, lineHeight: 1.05, margin: '0 0 24px',
          fontWeight: 700, letterSpacing: '-0.02em',
        }}>This.</h1>
        <p style={{ fontSize: 28, lineHeight: 1.35, margin: '0 0 18px' }}>
          This is your <em>website</em>.
        </p>
        <p style={{ fontSize: 18, lineHeight: 1.55, margin: '0 0 14px', color: '#333' }}>
          It's where people can come to learn about you, your work, the things you make.
        </p>
        <p style={{ fontSize: 18, lineHeight: 1.55, margin: '0 0 14px', color: '#333' }}>
          It's a small corner of the web that belongs to you — a public notebook, a
          studio window, a place to write words that aren't filtered by a feed.
        </p>
        <p style={{ fontSize: 18, lineHeight: 1.55, margin: '0 0 28px', color: '#333' }}>
          Tessera is the browser that rendered this page. Welcome.
        </p>
        <hr style={{ border: 0, borderTop: '1px solid #e5e5e5', margin: '32px 0' }} />
        <p style={{ fontSize: 13, color: '#888' }}>
          words.html · 4.2 KB · served from coast-1.justinjackson.ca · 318 ms
        </p>
      </div>
    </div>
  );
}

/* Shimmer keyframes — injected once. */
if (typeof document !== 'undefined' && !document.getElementById('tessera-keyframes')) {
  const s = document.createElement('style');
  s.id = 'tessera-keyframes';
  s.textContent = `
    @keyframes shimmer { from { background-position: 200% 0 } to { background-position: -200% 0 } }
    @keyframes blink { 50% { opacity: 0 } }
  `;
  document.head.appendChild(s);
}

/* ─── Status bar (bottom) ────────────────────────────────────────── */
function StatusBar({ url, bytes = '4.2 kB', dom = 38, hint }) {
  return (
    <div style={{
      height: 24, padding: '0 12px',
      display: 'flex', alignItems: 'center', gap: 16,
      borderTop: '1px solid var(--border)',
      background: 'var(--panel)',
      color: 'var(--muted)',
      fontFamily: 'var(--font-mono)',
      fontSize: 'var(--fs-xs)',
    }}>
      <span style={{ color: 'var(--text-2)' }}>{hint || '↪ link to ' + url}</span>
      <span style={{ flex: 1 }} />
      <span><b style={{ color: 'var(--text)' }}>{dom}</b> DOM</span>
      <span>·</span>
      <span><b style={{ color: 'var(--text)' }}>{bytes}</b></span>
      <span>·</span>
      <span><b style={{ color: 'var(--text)' }}>318</b>ms TTFB</span>
      <span>·</span>
      <span><b style={{ color: 'var(--text)' }}>16.4</b>MB heap</span>
    </div>
  );
}

/* Export atoms */
Object.assign(window, {
  Icon, ICONS, IconBtn,
  BuildPill, UrlBar, MiniLoadChart,
  TabStripA, TabStripB, Favicon, Webview, StatusBar,
});
