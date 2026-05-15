/* global React, ReactDOM, DesignCanvas, DCSection, DCArtboard */
// app.jsx — Tessera design exploration (B · Sidecar variant)
// Two sections of the same chrome variant, themed dark vs light, so the
// chrome+devtools system can be evaluated side-by-side under each theme.

const { useState } = React;

/* ─── Variation B — vertical sidebar tabs, devtools docks right ──── */
function FrameB({ devtools = null, panel, page = 'rendered' }) {
  const tabs = [
    { id: 't1', host: 'justinjackson.ca', title: 'Words — Justin Jackson' },
    { id: 't2', host: 'tessera.dev',      title: 'M3 release notes', audio: true },
    { id: 't3', host: 'github.com',       title: 'tessera-browser/tessera' },
    { id: 't4', host: 'localhost',        title: 'localhost:3000 · dev' },
  ];
  const pinned = [
    { id: 'p1', host: 'mail.fastmail.com', title: 'Mail' },
    { id: 'p2', host: 'cal.tessera.dev',   title: 'Calendar' },
  ];
  return (
    <div style={{
      display: 'flex',
      width: '100%', height: '100%',
      background: 'var(--bg)',
      color: 'var(--text)',
    }}>
      {/* sidebar */}
      <div style={{ paddingTop: 22, display: 'flex' }}>
        <TabStripB tabs={tabs} activeId="t1" pinned={pinned} />
      </div>

      {/* main column */}
      <div style={{
        flex: 1, minWidth: 0,
        display: 'flex', flexDirection: 'column',
      }}>
        {/* slim toolbar */}
        <div style={{
          height: 44, padding: '0 12px',
          display: 'flex', alignItems: 'center', gap: 8,
        }}>
          <IconBtn name="back" label="Back" />
          <IconBtn name="fwd"  label="Forward" />
          <IconBtn name="reload" label="Reload" />
          <UrlBar
            url={{ scheme: 'https', host: 'justinjackson.ca', path: '/words.html' }}
            loading={page === 'loading'}
            phases={[
              { t: 0,   dur: 24,  cat: 'net' },
              { t: 24,  dur: 36,  cat: 'net' },
              { t: 60,  dur: 58,  cat: 'net' },
              { t: 118, dur: 82,  cat: 'html' },
              { t: 200, dur: 38,  cat: 'js' },
              { t: 238, dur: 46,  cat: 'css' },
              { t: 284, dur: 64,  cat: 'layout' },
              { t: 348, dur: 28,  cat: 'paint' },
            ]}
            totalMs={376}
            variant="b"
          />
          <IconBtn name="star" label="Save" />
          <IconBtn name="more" label="More" />
        </div>

        {/* content + right-docked devtools */}
        <div style={{
          flex: 1, minHeight: 0,
          padding: '0 12px 8px',
          display: 'flex', gap: 8,
        }}>
          <div style={{
            flex: devtools ? '1 1 50%' : 1,
            minWidth: 0,
            border: '1px solid var(--border)',
            borderRadius: 'var(--r)',
            overflow: 'hidden',
            background: 'var(--panel)',
            display: 'flex',
          }}>
            <Webview state={page} />
          </div>
          {devtools && (
            <div style={{
              flex: '1 1 50%', minWidth: 0,
              border: '1px solid var(--border)',
              borderRadius: 'var(--r)',
              overflow: 'hidden',
            }}>
              <DevTools active={panel} dock="right" />
            </div>
          )}
        </div>

        <StatusBar
          url="justinjackson.ca/words.html"
          bytes="4.2 kB"
          dom={87}
          hint={page === 'loading' ? 'Loading… 348 ms · paint pending' : '↪ link to /about.html'}
        />
      </div>
    </div>
  );
}

/* ─── macOS-style window wrapper ───────────────────────────────── */
function WinShell({ children, height = 760 }) {
  return (
    <div style={{
      width: '100%', height,
      background: 'var(--bg)',
      borderRadius: 12,
      overflow: 'hidden',
      position: 'relative',
      display: 'flex', flexDirection: 'column',
      boxShadow: 'var(--sh-3)',
    }}>
      <div style={{
        position: 'absolute', top: 12, left: 14,
        display: 'flex', gap: 8, zIndex: 10,
      }}>
        {['#ff5f57', '#febc2e', '#28c840'].map(c => (
          <span key={c} style={{
            width: 12, height: 12, borderRadius: 6, background: c,
          }} />
        ))}
      </div>
      {children}
    </div>
  );
}

/* ─── Tweaks (themed-agnostic; lives outside the canvas world) ─── */
function Tweaks({ value, set }) {
  const Seg = ({ k, options }) => (
    <div style={{
      display: 'inline-flex',
      padding: 2,
      borderRadius: 8,
      background: 'rgba(0,0,0,0.05)',
      border: '1px solid rgba(0,0,0,0.08)',
      gap: 2,
    }}>
      {options.map(o => {
        const active = value[k] === o.v;
        return (
          <button key={o.v} onClick={() => set({ ...value, [k]: o.v })} style={{
            padding: '5px 10px',
            borderRadius: 6,
            background: active ? '#fff' : 'transparent',
            color: active ? '#1a1b1e' : '#71717a',
            fontSize: 12,
            fontWeight: active ? 600 : 500,
            boxShadow: active ? '0 1px 2px rgba(0,0,0,0.08)' : 'none',
            cursor: 'pointer', border: 0, font: 'inherit',
            transition: 'all .12s',
          }}>{o.l}</button>
        );
      })}
    </div>
  );
  return (
    <div style={{
      position: 'fixed', bottom: 16, right: 16, zIndex: 1000,
      padding: 10,
      borderRadius: 12,
      background: 'rgba(255,255,255,0.95)',
      backdropFilter: 'blur(20px)',
      border: '1px solid rgba(0,0,0,0.08)',
      boxShadow: '0 10px 30px rgba(0,0,0,0.12), 0 2px 6px rgba(0,0,0,0.04)',
      display: 'flex', flexDirection: 'column', gap: 6,
      fontFamily: '-apple-system, BlinkMacSystemFont, system-ui, sans-serif',
    }}>
      <div style={{
        display: 'flex', alignItems: 'center', gap: 8,
        fontSize: 11, fontWeight: 600,
        color: '#71717a', letterSpacing: '0.06em', textTransform: 'uppercase',
      }}>
        <span style={{ width: 6, height: 6, borderRadius: 3, background: '#7dd3a0' }} />
        Tweaks
      </div>
      <Row label="Theme">
        <Seg k="theme" options={[
          { v: 'auto',     l: 'Per-section' },
          { v: 'dark',     l: 'Dark' },
          { v: 'light',    l: 'Light' },
          { v: 'contrast', l: 'Contrast' },
        ]} />
      </Row>
      <Row label="Density">
        <Seg k="density" options={[
          { v: 'comfy',   l: 'Comfy' },
          { v: 'compact', l: 'Compact' },
        ]} />
      </Row>
      <Row label="Type">
        <Seg k="type" options={[
          { v: 'sans', l: 'Sans' },
          { v: 'mono', l: 'Mono' },
        ]} />
      </Row>
    </div>
  );
}
function Row({ label, children }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
      <span style={{ fontSize: 12, color: '#52525b', minWidth: 56 }}>{label}</span>
      {children}
    </div>
  );
}

/* ─── Themed root — wraps an artboard's content under a fixed theme.
   Tweaks panel can OVERRIDE the section theme — when tweaks.theme is set
   to something explicit (dark/light/contrast), every artboard gets that
   theme; otherwise each artboard uses its own `lockTheme` so the two
   sections stay dark-vs-light side by side. */
function Themed({ tweaks, lockTheme, children }) {
  const theme = tweaks.theme === 'auto' ? lockTheme : tweaks.theme;
  return (
    <div className="tessera"
         data-theme={theme}
         data-density={tweaks.density}
         data-type={tweaks.type}
         style={{ width: '100%', height: '100%' }}>
      {children}
    </div>
  );
}

/* ─── Root: design canvas with both themed sections ──────────────── */
function App() {
  const [tweaks, setTweaks] = useState({
    theme: 'auto',          // 'auto' = follow each section's lockTheme
    density: 'comfy',
    type: 'sans',
  });

  // Helper that mounts a frame with a given lock theme + page state.
  const Board = ({ lock, ...frameProps }) => (
    <Themed tweaks={tweaks} lockTheme={lock}>
      <WinShell><FrameB {...frameProps} /></WinShell>
    </Themed>
  );

  return (
    <>
      <Tweaks value={tweaks} set={setTweaks} />
      <DesignCanvas storageKey="tessera-canvas-b">

        <DCSection
          id="dark"
          title="B · Sidecar — Dark"
          subtitle="Vertical tabs · devtools docks right · single-column with command-palette search">

          <DCArtboard id="d-idle" label="Idle browsing" width={1280} height={760}>
            <Board lock="dark" page="rendered" />
          </DCArtboard>

          <DCArtboard id="d-loading" label="Loading · mini flame chart in URL bar" width={1280} height={760}>
            <Board lock="dark" page="loading" />
          </DCArtboard>

          <DCArtboard id="d-perf" label="DevTools · Performance" width={1280} height={760}>
            <Board lock="dark" devtools panel="perf" page="rendered" />
          </DCArtboard>

          <DCArtboard id="d-console" label="DevTools · Console" width={1280} height={760}>
            <Board lock="dark" devtools panel="console" page="rendered" />
          </DCArtboard>

          <DCArtboard id="d-internal" label="DevTools · Internals" width={1280} height={760}>
            <Board lock="dark" devtools panel="internal" page="rendered" />
          </DCArtboard>
        </DCSection>

        <DCSection
          id="light"
          title="B · Sidecar — Light"
          subtitle="Same chrome + devtools on warm-paper light theme. Toggle the Tweaks panel to override.">

          <DCArtboard id="l-idle" label="Idle browsing" width={1280} height={760}>
            <Board lock="light" page="rendered" />
          </DCArtboard>

          <DCArtboard id="l-loading" label="Loading · mini flame chart in URL bar" width={1280} height={760}>
            <Board lock="light" page="loading" />
          </DCArtboard>

          <DCArtboard id="l-perf" label="DevTools · Performance" width={1280} height={760}>
            <Board lock="light" devtools panel="perf" page="rendered" />
          </DCArtboard>

          <DCArtboard id="l-console" label="DevTools · Console" width={1280} height={760}>
            <Board lock="light" devtools panel="console" page="rendered" />
          </DCArtboard>

          <DCArtboard id="l-internal" label="DevTools · Internals" width={1280} height={760}>
            <Board lock="light" devtools panel="internal" page="rendered" />
          </DCArtboard>
        </DCSection>
      </DesignCanvas>
    </>
  );
}

const root = ReactDOM.createRoot(document.getElementById('root'));
root.render(<App />);
