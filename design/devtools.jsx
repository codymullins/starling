/* global React */
// devtools.jsx — Tessera DevTools panels
// Three panels users asked for:
//   1. Performance timeline (hero flame chart, paint/layout/script/GC)
//   2. Console + structured logs
//   3. Browser-internal debug (parser, JS, GC, IPC)
// Plus a shared DevTools shell with tabs, toolbar, and dock controls.

const { useMemo: _useMemo } = React;

/* ─── Realistic-looking performance sample ─────────────────────────
   ~600ms wall time, sampled at sub-ms resolution. Frame structure:
   net → parse → script → style → layout → paint → composite.
   Built as data so it lays out into flame bars + waterfall consistently. */
const PERF = {
  totalMs: 612,
  threads: [
    { name: 'Main', rows: [
      // top layer: tasks
      [
        { t: 0,   d: 6,   cat: 'net',    label: 'send req' },
        { t: 6,   d: 18,  cat: 'net',    label: 'TLS handshake' },
        { t: 24,  d: 12,  cat: 'net',    label: 'header read' },
        { t: 36,  d: 82,  cat: 'html',   label: 'parse HTML' },
        { t: 118, d: 14,  cat: 'css',    label: 'parse css' },
        { t: 132, d: 38,  cat: 'js',     label: 'eval app.js' },
        { t: 170, d: 4,   cat: 'gc',     label: 'minor GC' },
        { t: 174, d: 46,  cat: 'css',    label: 'recalc style' },
        { t: 220, d: 64,  cat: 'layout', label: 'layout flow' },
        { t: 284, d: 28,  cat: 'paint',  label: 'paint' },
        { t: 312, d: 14,  cat: 'paint',  label: 'composite' },
        // second frame
        { t: 380, d: 22,  cat: 'js',     label: 'rAF cb' },
        { t: 402, d: 8,   cat: 'css',    label: 'recalc' },
        { t: 410, d: 12,  cat: 'layout', label: 'incr layout' },
        { t: 422, d: 18,  cat: 'paint',  label: 'paint' },
        { t: 440, d: 8,   cat: 'paint',  label: 'composite' },
        // third frame, with GC spike
        { t: 500, d: 4,   cat: 'js',     label: 'timer' },
        { t: 504, d: 26,  cat: 'gc',     label: 'major GC · 4.2 MB' },
        { t: 530, d: 32,  cat: 'layout', label: 'layout' },
        { t: 562, d: 20,  cat: 'paint',  label: 'paint' },
        { t: 582, d: 12,  cat: 'paint',  label: 'composite' },
      ],
      // call-stack layer (script details)
      [
        { t: 132, d: 12, cat: 'js', label: 'init()' },
        { t: 144, d: 18, cat: 'js', label: 'hydrate(root)' },
        { t: 162, d: 8,  cat: 'js', label: 'queueMicrotask' },
        { t: 380, d: 14, cat: 'js', label: 'render()' },
        { t: 394, d: 8,  cat: 'js', label: 'diff()' },
      ],
      // deepest layer
      [
        { t: 146, d: 6, cat: 'js', label: 'build tree' },
        { t: 152, d: 10, cat: 'js', label: 'attach' },
        { t: 382, d: 10, cat: 'js', label: 'reconcile' },
      ],
    ]},
    { name: 'Loader', rows: [
      [
        { t: 0,   d: 24, cat: 'net', label: 'DNS · words.html' },
        { t: 24,  d: 36, cat: 'net', label: 'TLS' },
        { t: 60,  d: 58, cat: 'net', label: 'GET words.html' },
        { t: 118, d: 38, cat: 'net', label: 'GET style.css' },
        { t: 118, d: 96, cat: 'net', label: 'GET app.js' },
        { t: 214, d: 142,cat: 'net', label: 'GET hero.webp' },
        { t: 360, d: 22, cat: 'net', label: 'GET font.woff2' },
      ],
    ]},
    { name: 'Compositor', rows: [
      [
        { t: 312, d: 14, cat: 'paint', label: 'first paint' },
        { t: 440, d: 8,  cat: 'paint', label: 'commit' },
        { t: 582, d: 12, cat: 'paint', label: 'commit' },
      ],
    ]},
  ],
  frames: [
    { t: 0,   d: 326, fps: 60 },
    { t: 326, d: 122, fps: 60 },
    { t: 448, d: 164, fps: 47, jank: true },
  ],
  markers: [
    { t: 36,  label: 'FB',  hint: 'first byte' },
    { t: 312, label: 'FCP', hint: 'first contentful paint' },
    { t: 448, label: 'LCP', hint: 'largest contentful paint' },
    { t: 600, label: 'TTI', hint: 'time to interactive' },
  ],
};

/* ─── Flame chart core ────────────────────────────────────────────
   One row of bars, positioned proportionally. Used by Performance and
   reused by the URL-bar mini chart. */
function FlameRow({ row, total, height = 18, scale = 1 }) {
  return (
    <div style={{
      position: 'relative', height, marginBottom: 2,
    }}>
      {row.map((b, i) => {
        const w = (b.d / total) * 100 * scale;
        const x = (b.t / total) * 100 * scale;
        const showLabel = w > 4;
        return (
          <div key={i} title={`${b.label} · ${b.d}ms`} style={{
            position: 'absolute', left: `${x}%`, width: `${w}%`,
            top: 0, bottom: 0,
            background: `var(--cat-${b.cat})`,
            borderRadius: 2,
            color: 'var(--bar-ink, #0a0a0a)',
            fontFamily: 'var(--font-mono)',
            fontSize: 10,
            fontWeight: 500,
            padding: '0 4px',
            display: 'flex', alignItems: 'center',
            overflow: 'hidden', whiteSpace: 'nowrap',
            boxShadow: '0 0 0 0.5px rgba(0,0,0,0.25) inset',
          }}>
            {showLabel && b.label}
          </div>
        );
      })}
    </div>
  );
}

/* ─── Performance panel ─────────────────────────────────────────── */
function PerformancePanel({ height = 360 }) {
  const total = PERF.totalMs;
  // Timeline ruler: every 50ms
  const ticks = [];
  for (let t = 0; t <= total; t += 50) ticks.push(t);

  return (
    <div style={{
      display: 'flex', flexDirection: 'column',
      height: '100%', minHeight: 0,
    }}>
      {/* toolbar */}
      <div style={{
        height: 36, padding: '0 var(--pad-sm)',
        display: 'flex', alignItems: 'center', gap: 8,
        borderBottom: '1px solid var(--border)',
      }}>
        <button style={{
          height: 22, padding: '0 8px',
          display: 'inline-flex', alignItems: 'center', gap: 6,
          borderRadius: 'var(--r-sm)',
          background: 'var(--err)', color: '#fff',
          fontSize: 'var(--fs-xs)', fontWeight: 600,
        }}>
          <span style={{ width: 8, height: 8, borderRadius: 4, background: '#fff' }} />
          REC
        </button>
        <div className="stat"><b>{total}</b>ms <span>·</span> <b>3</b> frames <span>·</span> <b style={{ color: 'var(--warn)' }}>1</b> jank</div>
        <span style={{ flex: 1 }} />
        <div style={{ display: 'flex', alignItems: 'center', gap: 4, fontSize: 'var(--fs-xs)', color: 'var(--muted)' }}>
          {[
            ['html', 'HTML'], ['css','CSS'], ['js','JS'],
            ['layout','Layout'], ['paint','Paint'], ['gc','GC'], ['net','Net'],
          ].map(([k, label]) => (
            <span key={k} style={{
              display: 'inline-flex', alignItems: 'center', gap: 4,
              padding: '2px 6px', borderRadius: 'var(--r-sm)',
              background: 'var(--surface)', border: '1px solid var(--border)',
            }}>
              <span style={{ width: 8, height: 8, borderRadius: 2, background: `var(--cat-${k})` }} />
              {label}
            </span>
          ))}
        </div>
      </div>

      {/* frames strip */}
      <div style={{
        position: 'relative', height: 28,
        borderBottom: '1px solid var(--border)',
        padding: '4px 0',
      }}>
        {PERF.frames.map((f, i) => (
          <div key={i} style={{
            position: 'absolute',
            left: `${(f.t / total) * 100}%`,
            width: `${(f.d / total) * 100}%`,
            top: 4, bottom: 4,
            background: f.jank ? 'rgba(245,185,66,0.16)' : 'rgba(125,211,160,0.10)',
            borderLeft: '1px solid var(--border)',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            color: f.jank ? 'var(--warn)' : 'var(--muted)',
            fontFamily: 'var(--font-mono)', fontSize: 10,
          }}>{f.fps}fps · {f.d}ms{f.jank ? ' ⚠' : ''}</div>
        ))}
      </div>

      {/* ruler */}
      <div style={{
        position: 'relative', height: 18,
        borderBottom: '1px solid var(--border)',
        color: 'var(--faint)',
        fontFamily: 'var(--font-mono)', fontSize: 9,
      }}>
        {ticks.map(t => (
          <div key={t} style={{
            position: 'absolute', left: `${(t / total) * 100}%`,
            top: 0, bottom: 0,
            borderLeft: '1px solid var(--border)',
            paddingLeft: 3,
          }}>{t}ms</div>
        ))}
        {PERF.markers.map(m => (
          <div key={m.label} title={m.hint} style={{
            position: 'absolute', left: `${(m.t / total) * 100}%`,
            top: 0, bottom: 0,
            borderLeft: '1.5px dashed var(--accent)',
            color: 'var(--accent)', fontWeight: 600,
            paddingLeft: 3,
          }}>{m.label}</div>
        ))}
      </div>

      {/* threads + rows */}
      <div style={{
        flex: 1, minHeight: 0, overflow: 'auto',
        padding: '6px 0',
      }}>
        {PERF.threads.map((thr, ti) => (
          <div key={thr.name} style={{ marginBottom: 8 }}>
            <div style={{
              padding: '4px 10px 6px',
              color: 'var(--muted)',
              fontSize: 'var(--fs-xs)',
              fontFamily: 'var(--font-mono)',
              display: 'flex', alignItems: 'center', gap: 8,
            }}>
              <span style={{ color: 'var(--text-2)' }}>{thr.name}</span>
              <span style={{ opacity: 0.5 }}>thread</span>
            </div>
            <div style={{ padding: '0 10px' }}>
              {thr.rows.map((row, ri) => (
                <FlameRow key={ri} row={row} total={total} height={18} />
              ))}
            </div>
          </div>
        ))}
      </div>

      {/* selected event detail */}
      <div style={{
        height: 88, borderTop: '1px solid var(--border)',
        padding: 'var(--pad-sm) var(--pad)',
        display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 16,
        fontFamily: 'var(--font-mono)', fontSize: 'var(--fs-xs)',
      }}>
        <div>
          <div style={{ color: 'var(--muted)', marginBottom: 4 }}>SELECTED · layout flow</div>
          <div style={{ color: 'var(--text)', fontSize: 'var(--fs-md)', marginBottom: 2 }}>BlockFlow::layout(<span style={{ color: 'var(--cat-layout)' }}>root</span>)</div>
          <div style={{ color: 'var(--muted)' }}>libtessera/layout/flow.cc:128</div>
        </div>
        <div>
          <div style={{ color: 'var(--muted)', marginBottom: 4 }}>TIMING</div>
          <div>start <b style={{ color: 'var(--text)' }}>220.4</b>ms</div>
          <div>self <b style={{ color: 'var(--text)' }}>64.1</b>ms · total <b style={{ color: 'var(--text)' }}>64.1</b>ms</div>
          <div style={{ color: 'var(--warn)' }}>forced reflow (1×) from app.js:42</div>
        </div>
        <div>
          <div style={{ color: 'var(--muted)', marginBottom: 4 }}>CALL TREE</div>
          <div>↳ LayoutEngine::run()  <span style={{ color: 'var(--muted)' }}>2.1ms</span></div>
          <div style={{ paddingLeft: 12 }}>↳ BlockFlow::layout  <span style={{ color: 'var(--muted)' }}>62.0ms</span></div>
          <div style={{ paddingLeft: 24 }}>↳ InlineFormat::run  <span style={{ color: 'var(--muted)' }}>41.6ms</span></div>
        </div>
      </div>
    </div>
  );
}

/* ─── Console / structured logs panel ────────────────────────────── */
const LOGS = [
  { t: '00:00.012', lvl: 'info',  src: 'tessera', cat: 'boot',   msg: 'engine ready · M3 (flow-layout, async-loader, ipc-sandbox)' },
  { t: '00:00.024', lvl: 'info',  src: 'loader',  cat: 'net',    msg: 'GET justinjackson.ca/words.html', tag: '200 · 4.2kB · 318ms' },
  { t: '00:00.036', lvl: 'info',  src: 'parser',  cat: 'html',   msg: 'tokens=412 nodes=87 errors=0' },
  { t: '00:00.118', lvl: 'warn',  src: 'parser',  cat: 'html',   msg: 'unmatched <em> at line 18 · auto-closed' },
  { t: '00:00.132', lvl: 'log',   src: 'page',    cat: 'console',msg: '[app] booted in 4.2ms' },
  { t: '00:00.146', lvl: 'log',   src: 'page',    cat: 'console',msg: '{ user: { id: 4082, plan: \'free\' }, flags: [\'ab.cta-v2\', \'metrics\'] }', obj: true },
  { t: '00:00.170', lvl: 'debug', src: 'gc',      cat: 'gc',     msg: 'minor · 1.8MB → 1.2MB · 4.1ms' },
  { t: '00:00.220', lvl: 'info',  src: 'layout',  cat: 'layout', msg: 'flow pass · 87 nodes · 64.1ms' },
  { t: '00:00.221', lvl: 'warn',  src: 'layout',  cat: 'layout', msg: 'forced reflow from app.js:42 — read offsetHeight inside RAF' },
  { t: '00:00.284', lvl: 'info',  src: 'paint',   cat: 'paint',  msg: 'first paint · 28.0ms · 4 layers' },
  { t: '00:00.382', lvl: 'log',   src: 'page',    cat: 'console',msg: 'fetch("/api/me") → 200', tag: '34ms' },
  { t: '00:00.504', lvl: 'error', src: 'js',      cat: 'js',     msg: 'TypeError: Cannot read property \'tag\' of undefined' },
  { t: '00:00.504', lvl: 'error', src: 'js',      cat: 'js',     msg: '    at Hero.render (app.js:142:18)' },
  { t: '00:00.530', lvl: 'info',  src: 'ipc',     cat: 'ipc',    msg: 'WebContent → UI · paint-ack #218 · 0.4ms' },
];

const LVL_COLOR = {
  info: 'var(--muted)', log: 'var(--text-2)', warn: 'var(--warn)',
  error: 'var(--err)', debug: 'var(--cat-css)',
};

function ConsolePanel() {
  return (
    <div style={{
      display: 'flex', flexDirection: 'column',
      height: '100%', minHeight: 0,
    }}>
      {/* toolbar */}
      <div style={{
        height: 36, padding: '0 var(--pad-sm)',
        display: 'flex', alignItems: 'center', gap: 8,
        borderBottom: '1px solid var(--border)',
        fontSize: 'var(--fs-xs)',
      }}>
        {[
          { k: 'all', n: 'all',   c: 'var(--text)', count: LOGS.length },
          { k: 'err', n: 'error', c: 'var(--err)',  count: 2 },
          { k: 'wrn', n: 'warn',  c: 'var(--warn)', count: 2 },
          { k: 'inf', n: 'info',  c: 'var(--muted)', count: 7 },
          { k: 'dbg', n: 'debug', c: 'var(--cat-css)', count: 3 },
        ].map((f, i) => (
          <button key={f.k} style={{
            display: 'inline-flex', alignItems: 'center', gap: 5,
            padding: '3px 8px',
            borderRadius: 'var(--r-pill)',
            background: i === 0 ? 'var(--surface)' : 'transparent',
            border: i === 0 ? '1px solid var(--border)' : '1px solid transparent',
            color: 'var(--text-2)',
            fontFamily: 'var(--font-mono)',
          }}>
            <span style={{ width: 6, height: 6, borderRadius: 3, background: f.c }} />
            {f.n} <span style={{ color: 'var(--faint)' }}>{f.count}</span>
          </button>
        ))}
        <span style={{ flex: 1 }} />
        <div style={{
          display: 'flex', alignItems: 'center', gap: 6,
          padding: '0 8px', height: 22,
          borderRadius: 'var(--r-sm)',
          background: 'var(--surface)',
          border: '1px solid var(--border)',
          color: 'var(--muted)',
          fontFamily: 'var(--font-mono)',
        }}>
          <span style={{ opacity: 0.6 }}>filter</span>
          <span style={{ color: 'var(--text)' }}>src:</span>
          <span style={{ color: 'var(--accent)' }}>layout</span>
        </div>
      </div>

      {/* log rows */}
      <div style={{
        flex: 1, minHeight: 0, overflow: 'auto',
        fontFamily: 'var(--font-mono)',
        fontSize: 'var(--fs-sm)',
      }}>
        {LOGS.map((l, i) => (
          <div key={i} style={{
            display: 'grid',
            gridTemplateColumns: '76px 64px 64px 1fr auto',
            gap: 10,
            padding: '4px 12px',
            borderBottom: '1px solid var(--border)',
            background: l.lvl === 'error' ? 'rgba(239,111,122,0.06)'
                       : l.lvl === 'warn' ? 'rgba(245,185,66,0.05)'
                       : 'transparent',
            alignItems: 'baseline',
          }}>
            <span style={{ color: 'var(--faint)' }}>{l.t}</span>
            <span style={{ color: LVL_COLOR[l.lvl], fontWeight: 500 }}>{l.lvl}</span>
            <span style={{
              color: `var(--cat-${l.cat === 'console' ? 'js' : l.cat === 'boot' ? 'idle' : l.cat})`,
              fontWeight: 500,
              fontSize: 'var(--fs-xs)',
            }}>{l.src}</span>
            <span style={{
              color: l.obj ? 'var(--cat-css)' : 'var(--text)',
              whiteSpace: 'pre-wrap', wordBreak: 'break-word',
            }}>{l.msg}</span>
            {l.tag && <span style={{
              color: 'var(--muted)',
              fontSize: 'var(--fs-xs)',
            }}>{l.tag}</span>}
          </div>
        ))}
      </div>

      {/* prompt */}
      <div style={{
        height: 30, padding: '0 12px',
        borderTop: '1px solid var(--border)',
        display: 'flex', alignItems: 'center', gap: 8,
        background: 'var(--surface)',
        fontFamily: 'var(--font-mono)',
        fontSize: 'var(--fs-sm)',
      }}>
        <span style={{ color: 'var(--accent)' }}>›</span>
        <span style={{ color: 'var(--text)' }}>document.fonts.ready.then(</span>
        <span style={{
          width: 7, height: 14, background: 'var(--accent)',
          animation: 'blink 1s steps(2) infinite',
        }} />
      </div>
    </div>
  );
}

/* ─── Internal debug panel ───────────────────────────────────────── */
function InternalPanel() {
  // Four sub-modules: Parser, JS, GC, IPC, displayed as cards in a grid.
  return (
    <div style={{
      display: 'flex', flexDirection: 'column',
      height: '100%', minHeight: 0,
    }}>
      <div style={{
        height: 36, padding: '0 var(--pad-sm)',
        display: 'flex', alignItems: 'center', gap: 8,
        borderBottom: '1px solid var(--border)',
        fontSize: 'var(--fs-xs)',
        color: 'var(--muted)',
        fontFamily: 'var(--font-mono)',
      }}>
        {['parser', 'js', 'style', 'layout', 'paint', 'gc', 'ipc', 'sandbox'].map((m, i) => (
          <button key={m} style={{
            padding: '3px 10px',
            borderRadius: 'var(--r-pill)',
            background: i === 0 ? 'var(--accent-bg)' : 'transparent',
            border: i === 0 ? '1px solid var(--accent-line)' : '1px solid transparent',
            color: i === 0 ? 'var(--accent)' : 'var(--text-2)',
            fontWeight: 500,
          }}>{m}</button>
        ))}
        <span style={{ flex: 1 }} />
        <span>step <b style={{ color: 'var(--text)' }}>F10</b> · break <b style={{ color: 'var(--text)' }}>F9</b></span>
      </div>

      <div style={{
        flex: 1, minHeight: 0, overflow: 'auto',
        padding: 'var(--pad-sm)',
        display: 'grid',
        gridTemplateColumns: '1fr 1fr',
        gridAutoRows: 'min-content',
        gap: 'var(--gap-sm)',
      }}>
        <ParserCard />
        <JSCard />
        <GCCard />
        <IPCCard />
      </div>
    </div>
  );
}

function Card({ title, badge, badgeColor, children }) {
  return (
    <div style={{
      border: '1px solid var(--border)',
      borderRadius: 'var(--r-md)',
      background: 'var(--surface)',
      overflow: 'hidden',
      display: 'flex', flexDirection: 'column',
    }}>
      <div style={{
        height: 28, padding: '0 10px',
        display: 'flex', alignItems: 'center', gap: 8,
        borderBottom: '1px solid var(--border)',
        background: 'var(--panel)',
      }}>
        <span style={{
          fontFamily: 'var(--font-mono)',
          fontSize: 'var(--fs-xs)',
          color: 'var(--muted)',
          textTransform: 'uppercase',
          letterSpacing: '0.06em',
          whiteSpace: 'nowrap',
          overflow: 'hidden', textOverflow: 'ellipsis',
          flex: '0 1 auto', minWidth: 0,
        }}>{title}</span>
        <span style={{ flex: 1 }} />
        {badge && <span style={{
          fontSize: 'var(--fs-xs)',
          fontFamily: 'var(--font-mono)',
          color: badgeColor || 'var(--muted)',
          whiteSpace: 'nowrap',
          flex: '0 0 auto',
        }}>{badge}</span>}
      </div>
      <div style={{ padding: 10 }}>{children}</div>
    </div>
  );
}

/* Parser — DOM tree of currently-parsed page, with parse-state pill. */
function ParserCard() {
  const tree = [
    { d: 0, tag: 'html', state: 'parsed' },
    { d: 1, tag: 'head', state: 'parsed' },
    { d: 2, tag: 'title', state: 'parsed', txt: 'Words' },
    { d: 2, tag: 'link rel=stylesheet', state: 'fetching', tag2: 'style.css' },
    { d: 1, tag: 'body', state: 'parsing' },
    { d: 2, tag: 'article', state: 'parsing' },
    { d: 3, tag: 'h1', state: 'parsed', txt: 'This.' },
    { d: 3, tag: 'p', state: 'parsed', txt: 'This is your website.' },
    { d: 3, tag: 'p', state: 'queued' },
    { d: 3, tag: 'p', state: 'queued' },
  ];
  const stateColor = {
    parsed: 'var(--ok)', parsing: 'var(--warn)', fetching: 'var(--cat-net)',
    queued: 'var(--faint)',
  };
  return (
    <Card title="parser · html5" badge="412 tok · 87 node · 0 err" badgeColor="var(--ok)">
      <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--fs-xs)', lineHeight: 1.6 }}>
        {tree.map((n, i) => (
          <div key={i} style={{
            display: 'flex', alignItems: 'center', gap: 6,
            paddingLeft: n.d * 12,
            color: n.state === 'queued' ? 'var(--faint)' : 'var(--text-2)',
          }}>
            <span style={{
              width: 6, height: 6, borderRadius: 3,
              background: stateColor[n.state],
              flex: '0 0 auto',
            }} />
            <span style={{ color: 'var(--cat-html)' }}>&lt;{n.tag}&gt;</span>
            {n.txt && <span style={{ color: 'var(--muted)' }}>"{n.txt}"</span>}
            {n.tag2 && <span style={{ color: 'var(--cat-net)', marginLeft: 'auto' }}>{n.tag2}</span>}
          </div>
        ))}
      </div>
    </Card>
  );
}

/* JS — call stack + heap usage + microtask queue */
function JSCard() {
  return (
    <Card title="js engine · cinder" badge="heap 16.4 / 64 MB" badgeColor="var(--text-2)">
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
        <div>
          <div style={{
            color: 'var(--muted)', fontFamily: 'var(--font-mono)',
            fontSize: 'var(--fs-xs)', marginBottom: 4,
          }}>CALL STACK</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--fs-xs)', lineHeight: 1.6 }}>
            {[
              ['Hero.render', 'app.js:142', 'var(--err)'],
              ['App.render',  'app.js:88',  'var(--text-2)'],
              ['hydrate',     'react.js:1284', 'var(--text-2)'],
              ['<rAF>',       'libtessera/event', 'var(--muted)'],
            ].map(([fn, src, c], i) => (
              <div key={i} style={{ display: 'flex', gap: 8, color: c }}>
                <span style={{ width: 12 }}>{i === 0 ? '▸' : ''}</span>
                <span style={{ flex: 1 }}>{fn}</span>
                <span style={{ color: 'var(--muted)' }}>{src}</span>
              </div>
            ))}
          </div>
        </div>
        <div>
          <div style={{
            color: 'var(--muted)', fontFamily: 'var(--font-mono)',
            fontSize: 'var(--fs-xs)', marginBottom: 4,
          }}>HEAP · 16.4 MB</div>
          <div style={{
            height: 8, background: 'var(--bg)', borderRadius: 4,
            overflow: 'hidden', display: 'flex', marginBottom: 8,
          }}>
            <span style={{ width: '38%', background: 'var(--cat-js)' }} />
            <span style={{ width: '24%', background: 'var(--cat-css)' }} />
            <span style={{ width: '14%', background: 'var(--cat-html)' }} />
            <span style={{ width: '10%', background: 'var(--cat-net)' }} />
          </div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 10, color: 'var(--muted)', lineHeight: 1.6 }}>
            <div><span style={{ color: 'var(--cat-js)' }}>■</span> JS objects 6.2</div>
            <div><span style={{ color: 'var(--cat-css)' }}>■</span> strings 3.9</div>
            <div><span style={{ color: 'var(--cat-html)' }}>■</span> DOM 2.3</div>
            <div><span style={{ color: 'var(--cat-net)' }}>■</span> buffers 1.6</div>
          </div>
        </div>
      </div>
    </Card>
  );
}

/* GC — recent GC events as sparkline */
function GCCard() {
  // synthetic GC events: pairs (t, freed_bytes_kb)
  const events = [
    { t: 8,  kb: 120, kind: 'minor' },
    { t: 24, kb: 240, kind: 'minor' },
    { t: 40, kb: 80,  kind: 'minor' },
    { t: 56, kb: 1200,kind: 'major' },
    { t: 72, kb: 180, kind: 'minor' },
    { t: 88, kb: 200, kind: 'minor' },
    { t: 104,kb: 4200,kind: 'major' },
  ];
  const max = Math.max(...events.map(e => e.kb));
  return (
    <Card title="garbage collector" badge="2 major · 5 minor · 38ms total" badgeColor="var(--warn)">
      <div style={{
        height: 64, display: 'flex', alignItems: 'flex-end', gap: 6,
        padding: '0 0 4px',
        borderBottom: '1px dashed var(--border)',
        marginBottom: 8,
      }}>
        {events.map((e, i) => (
          <div key={i} title={`${e.kind} · ${e.kb}kB`} style={{
            flex: 1,
            height: `${(e.kb / max) * 100}%`,
            background: e.kind === 'major' ? 'var(--cat-gc)' : 'var(--cat-css)',
            borderRadius: '2px 2px 0 0',
            position: 'relative',
          }}>
            {e.kind === 'major' && <span style={{
              position: 'absolute', top: -10, left: '50%',
              transform: 'translateX(-50%)',
              fontSize: 8, color: 'var(--cat-gc)',
              fontFamily: 'var(--font-mono)',
            }}>!</span>}
          </div>
        ))}
      </div>
      <div style={{
        display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 8,
        fontFamily: 'var(--font-mono)', fontSize: 'var(--fs-xs)',
      }}>
        <div>
          <div style={{ color: 'var(--muted)' }}>young gen</div>
          <div style={{ color: 'var(--text)' }}>4.1 / 8 MB</div>
        </div>
        <div>
          <div style={{ color: 'var(--muted)' }}>old gen</div>
          <div style={{ color: 'var(--text)' }}>12.3 / 56 MB</div>
        </div>
        <div>
          <div style={{ color: 'var(--muted)' }}>next gc</div>
          <div style={{ color: 'var(--text)' }}>~2.4s</div>
        </div>
      </div>
    </Card>
  );
}

/* IPC — channels with message rate sparklines */
function IPCCard() {
  const channels = [
    { from: 'WebContent', to: 'UI', msgs: 218, rate: 'paint-ack', cat: 'paint' },
    { from: 'UI', to: 'WebContent', msgs: 47, rate: 'input', cat: 'js' },
    { from: 'Loader', to: 'WebContent', msgs: 12, rate: 'data', cat: 'net' },
    { from: 'WebContent', to: 'Sandbox', msgs: 4, rate: 'fs', cat: 'gc' },
  ];
  return (
    <Card title="ipc · 4 channels" badge="281 msgs · 99.8% ok" badgeColor="var(--ok)">
      <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--fs-xs)' }}>
        {channels.map((c, i) => (
          <div key={i} style={{
            display: 'grid',
            gridTemplateColumns: '90px 14px 90px 1fr auto',
            gap: 6,
            padding: '4px 0',
            alignItems: 'center',
            borderBottom: i < channels.length - 1 ? '1px dashed var(--border)' : 'none',
          }}>
            <span style={{ color: 'var(--text-2)' }}>{c.from}</span>
            <span style={{ color: 'var(--muted)', textAlign: 'center' }}>→</span>
            <span style={{ color: 'var(--text-2)' }}>{c.to}</span>
            <div style={{
              height: 12, display: 'flex', alignItems: 'flex-end', gap: 1,
            }}>
              {Array.from({length: 24}).map((_, j) => {
                const h = 20 + Math.abs(Math.sin(i * 7 + j * 0.6)) * 80;
                return (
                  <span key={j} style={{
                    flex: 1, height: `${h}%`,
                    background: `var(--cat-${c.cat})`,
                    opacity: 0.7,
                    borderRadius: 1,
                  }} />
                );
              })}
            </div>
            <span style={{ color: 'var(--muted)' }}>{c.msgs}</span>
          </div>
        ))}
      </div>
    </Card>
  );
}

/* ─── DevTools shell ─────────────────────────────────────────────── */
function DevTools({ active = 'perf', dock = 'bottom' }) {
  const tabs = [
    { id: 'perf',    label: 'Performance', icon: 'spark' },
    { id: 'console', label: 'Console',     icon: 'console', count: 2 },
    { id: 'internal',label: 'Internals',   icon: 'cpu' },
    { id: 'inspect', label: 'Inspect',     icon: 'inspect', dim: true },
    { id: 'network', label: 'Network',     icon: 'layers',  dim: true },
  ];
  const Body = active === 'perf' ? PerformancePanel
             : active === 'console' ? ConsolePanel
             : InternalPanel;
  return (
    <div style={{
      display: 'flex', flexDirection: 'column',
      height: '100%', minHeight: 0,
      background: 'var(--panel)',
    }}>
      {/* tab strip + dock controls */}
      <div style={{
        height: 34, padding: '0 var(--pad-sm)',
        display: 'flex', alignItems: 'center', gap: 2,
        borderBottom: '1px solid var(--border)',
        background: 'var(--bg)',
      }}>
        {tabs.map(t => {
          const on = t.id === active;
          return (
            <button key={t.id} style={{
              display: 'inline-flex', alignItems: 'center', gap: 6,
              padding: '0 12px',
              height: 26,
              borderRadius: 'var(--r-sm)',
              background: on ? 'var(--panel)' : 'transparent',
              color: on ? 'var(--text)' : (t.dim ? 'var(--faint)' : 'var(--text-2)'),
              fontSize: 'var(--fs-sm)',
              fontWeight: on ? 500 : 400,
              position: 'relative',
            }}>
              <Icon d={ICONS[t.icon]} size={12}
                    style={{ color: on ? 'var(--accent)' : 'inherit' }} />
              {t.label}
              {t.count && <span style={{
                padding: '0 5px', height: 14,
                borderRadius: 7,
                background: 'var(--err)', color: '#fff',
                fontSize: 9, fontWeight: 600,
                display: 'inline-flex', alignItems: 'center',
                fontFamily: 'var(--font-mono)',
              }}>{t.count}</span>}
            </button>
          );
        })}
        <span style={{ flex: 1 }} />
        <IconBtn name={dock === 'bottom' ? 'panelB' : 'panelR'} label="Dock" />
        <IconBtn name="detach" label="Detach" />
        <IconBtn name="close" label="Close" />
      </div>

      {/* body */}
      <div style={{ flex: 1, minHeight: 0 }}>
        <Body />
      </div>
    </div>
  );
}

Object.assign(window, {
  PerformancePanel, ConsolePanel, InternalPanel, DevTools, PERF,
});
