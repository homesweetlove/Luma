using System.Text;
using Microsoft.Web.WebView2.Core;

namespace Luma.Services;

public sealed class HomeDashboardService
{
    public const string HostName = "luma.local";
    public const string HomeUrl = "https://luma.local/home.html";

    private readonly string _homeRoot;

    public HomeDashboardService(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _homeRoot = Path.Combine(dataRoot, "Home");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_homeRoot);
        var path = Path.Combine(_homeRoot, "home.html");
        await File.WriteAllTextAsync(path, HomeHtml, new UTF8Encoding(false), cancellationToken);
    }

    public void Configure(CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);
        core.SetVirtualHostNameToFolderMapping(
            HostName,
            _homeRoot,
            CoreWebView2HostResourceAccessKind.DenyCors);
    }

    public bool IsHome(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Host, HostName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.AbsolutePath, "/home.html", StringComparison.OrdinalIgnoreCase);

    private const string HomeHtml = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width,initial-scale=1" />
<meta http-equiv="Content-Security-Policy" content="default-src 'self'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'none'; object-src 'none'; frame-src 'none';" />
<title>Luma — Home</title>
<style>
:root {
  color-scheme: light dark;
  font-family: "Segoe UI Variable", "Segoe UI", system-ui, sans-serif;
  --text: #17191f;
  --muted: rgba(31,35,45,.62);
  --glass: rgba(255,255,255,.56);
  --glass-strong: rgba(255,255,255,.72);
  --line: rgba(255,255,255,.70);
  --shadow: 0 18px 60px rgba(60,66,88,.14);
  --accent: #6675f7;
  --accent-soft: rgba(102,117,247,.13);
}
@media (prefers-color-scheme: dark) {
  :root {
    --text: #f5f6fb;
    --muted: rgba(234,236,245,.62);
    --glass: rgba(24,26,34,.52);
    --glass-strong: rgba(28,30,39,.72);
    --line: rgba(255,255,255,.11);
    --shadow: 0 22px 70px rgba(0,0,0,.26);
    --accent: #9ca7ff;
    --accent-soft: rgba(156,167,255,.13);
  }
}
* { box-sizing: border-box; }
html, body { min-height: 100%; margin: 0; }
body {
  color: var(--text);
  overflow-x: hidden;
  background:
    radial-gradient(circle at 13% 12%, rgba(152,181,255,.34), transparent 32%),
    radial-gradient(circle at 82% 18%, rgba(220,173,255,.26), transparent 32%),
    radial-gradient(circle at 72% 82%, rgba(154,234,218,.24), transparent 34%),
    linear-gradient(145deg, #eef1f9 0%, #f7f4f5 48%, #edf4f5 100%);
}
@media (prefers-color-scheme: dark) {
  body {
    background:
      radial-gradient(circle at 13% 12%, rgba(73,95,185,.25), transparent 34%),
      radial-gradient(circle at 84% 18%, rgba(132,72,159,.18), transparent 33%),
      radial-gradient(circle at 72% 82%, rgba(44,121,111,.18), transparent 34%),
      linear-gradient(145deg, #0e1016 0%, #17161d 50%, #101719 100%);
  }
}
body::before, body::after {
  content: "";
  position: fixed;
  border-radius: 999px;
  pointer-events: none;
  filter: blur(14px);
  opacity: .58;
}
body::before { width: 230px; height: 230px; left: 5%; top: 44%; background: rgba(255,205,220,.22); }
body::after { width: 280px; height: 280px; right: -70px; bottom: -40px; background: rgba(126,184,255,.20); }
.shell { width: min(1180px, calc(100% - 48px)); margin: 0 auto; padding: 56px 0 46px; position: relative; z-index: 1; }
.hero { display: grid; grid-template-columns: 1fr auto; gap: 24px; align-items: end; margin-bottom: 26px; }
.eyebrow { font-size: 13px; font-weight: 650; letter-spacing: .12em; text-transform: uppercase; color: var(--muted); }
h1 { margin: 8px 0 4px; font-size: clamp(34px, 5vw, 60px); line-height: 1; letter-spacing: -.045em; font-weight: 650; }
.sub { color: var(--muted); font-size: 15px; }
.clock { text-align: right; }
#time { font-size: 28px; font-weight: 600; letter-spacing: -.03em; }
#date { margin-top: 4px; color: var(--muted); font-size: 13px; }
.glass {
  background: linear-gradient(145deg, var(--glass-strong), var(--glass));
  border: 1px solid var(--line);
  box-shadow: var(--shadow), inset 0 1px 0 rgba(255,255,255,.28);
  backdrop-filter: blur(24px) saturate(138%);
  -webkit-backdrop-filter: blur(24px) saturate(138%);
}
.search-wrap { border-radius: 24px; padding: 9px; display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: 10px; margin-bottom: 20px; }
.search-icon { width: 38px; height: 38px; border-radius: 14px; display: grid; place-items: center; background: var(--accent-soft); color: var(--accent); font-size: 18px; }
#search { border: 0; outline: 0; background: transparent; color: var(--text); font-size: 16px; width: 100%; padding: 10px 2px; }
#search::placeholder { color: var(--muted); }
.go, .soft-btn, .quick, .add-favorite {
  border: 1px solid var(--line); color: var(--text); cursor: pointer; font: inherit;
  background: rgba(255,255,255,.18); transition: transform .14s ease, background .14s ease, border-color .14s ease;
}
.go:hover, .soft-btn:hover, .quick:hover, .add-favorite:hover { transform: translateY(-1px); background: rgba(255,255,255,.28); }
.go:active, .soft-btn:active, .quick:active, .add-favorite:active { transform: translateY(0) scale(.985); }
.go { border-radius: 15px; padding: 10px 16px; font-weight: 600; }
.grid { display: grid; grid-template-columns: 1.15fr .85fr; gap: 18px; align-items: start; }
.column { display: grid; gap: 18px; }
.card { border-radius: 24px; padding: 20px; min-width: 0; }
.card-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 14px; }
.card-title { font-weight: 650; letter-spacing: -.01em; }
.card-hint { color: var(--muted); font-size: 12px; }
.favorites { display: grid; grid-template-columns: repeat(5, minmax(0,1fr)); gap: 10px; }
.favorite { text-decoration: none; color: var(--text); min-width: 0; padding: 12px 8px; border-radius: 18px; display: grid; justify-items: center; gap: 8px; transition: transform .15s ease, background .15s ease; }
.favorite:hover { background: rgba(255,255,255,.20); transform: translateY(-2px); }
.favicon { width: 42px; height: 42px; border-radius: 15px; background: rgba(255,255,255,.31); border: 1px solid var(--line); display: grid; place-items: center; font-weight: 700; color: var(--accent); box-shadow: inset 0 1px rgba(255,255,255,.35); }
.favorite-name { font-size: 12px; max-width: 100%; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
.add-favorite { width: 42px; height: 42px; border-radius: 15px; font-size: 20px; align-self: start; }
.quick-grid { display: grid; grid-template-columns: repeat(2,1fr); gap: 10px; }
.quick { min-height: 74px; border-radius: 18px; text-align: left; padding: 12px; }
.quick b { display: block; font-size: 13px; margin-top: 7px; }
.quick span { color: var(--muted); font-size: 11px; }
.recent-list { display: grid; gap: 8px; }
.recent { text-decoration: none; color: var(--text); display: flex; gap: 10px; align-items: center; padding: 10px 11px; border-radius: 15px; background: rgba(255,255,255,.12); }
.recent:hover { background: rgba(255,255,255,.22); }
.recent-dot { width: 9px; height: 9px; border-radius: 50%; background: var(--accent); box-shadow: 0 0 0 5px var(--accent-soft); margin: 0 4px; }
.recent-text { min-width: 0; }
.recent-title { font-size: 13px; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
.recent-url { color: var(--muted); font-size: 11px; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; margin-top: 2px; }
textarea { resize: vertical; min-height: 116px; width: 100%; border: 0; outline: 0; border-radius: 17px; padding: 13px 14px; color: var(--text); background: rgba(255,255,255,.13); font: inherit; line-height: 1.55; }
textarea::placeholder { color: var(--muted); }
.automation { display: grid; gap: 10px; }
.status-line { display: flex; align-items: center; gap: 9px; }
.status-orb { width: 10px; height: 10px; border-radius: 50%; background: #84c79c; box-shadow: 0 0 0 5px rgba(132,199,156,.13); }
.status-orb.busy { background: #8b94ff; box-shadow: 0 0 0 5px rgba(139,148,255,.13); }
.status-main { font-size: 13px; font-weight: 600; }
.status-copy { color: var(--muted); font-size: 12px; line-height: 1.45; }
.soft-btn { border-radius: 14px; padding: 9px 12px; font-size: 12px; width: max-content; }
.footer { display: flex; justify-content: space-between; gap: 12px; color: var(--muted); font-size: 11px; margin-top: 18px; padding: 0 5px; }
.fade { animation: rise .32s cubic-bezier(.2,.8,.2,1) both; }
@keyframes rise { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: translateY(0); } }
@media (max-width: 860px) { .grid { grid-template-columns: 1fr; } .favorites { grid-template-columns: repeat(4,1fr); } }
@media (max-width: 560px) { .shell { width: min(100% - 28px,1180px); padding-top: 32px; } .hero { grid-template-columns: 1fr; } .clock { text-align: left; } .favorites { grid-template-columns: repeat(3,1fr); } }
@media (prefers-reduced-motion: reduce) { * { animation: none !important; transition: none !important; } }
</style>
</head>
<body>
<main class="shell">
  <section class="hero fade">
    <div>
      <div class="eyebrow">Luma home</div>
      <h1 id="greeting">Good morning.</h1>
      <div class="sub">Browse normally. Automate only when it helps.</div>
    </div>
    <div class="clock"><div id="time">--:--</div><div id="date">Loading date…</div></div>
  </section>

  <form id="searchForm" class="search-wrap glass fade" autocomplete="off">
    <div class="search-icon">⌕</div>
    <input id="search" aria-label="Search or enter a website" placeholder="Search the web or enter a website…" autofocus />
    <button class="go" type="submit">Go</button>
  </form>

  <div class="grid">
    <div class="column">
      <section class="card glass fade">
        <div class="card-head"><div><div class="card-title">Favorites</div><div class="card-hint">Kept only in this Luma profile</div></div><button id="addFavorite" class="add-favorite" title="Add favorite">+</button></div>
        <div id="favorites" class="favorites"></div>
      </section>

      <section class="card glass fade">
        <div class="card-head"><div><div class="card-title">Quick actions</div><div class="card-hint">Local, visible and bounded</div></div></div>
        <div class="quick-grid">
          <button class="quick" data-action="new-search">⌕<b>New search</b><span>Open a clean web tab</span></button>
          <button class="quick" data-action="record">●<b>Record macro</b><span>Start from a fresh tab</span></button>
          <button class="quick" data-action="watch">◉<b>Watch page</b><span>Open foreground watch</span></button>
          <button class="quick" data-action="vault">◇<b>Local Vault</b><span>Open protected slots</span></button>
          <button class="quick" data-action="ai">✦<b>Ask / delegate</b><span>Open the Luma panel</span></button>
          <button class="quick" data-action="hydrate">↻<b>Refresh status</b><span>Recent sites and automation</span></button>
        </div>
      </section>

      <section class="card glass fade">
        <div class="card-head"><div><div class="card-title">Recent</div><div class="card-hint">Origins from the local audit log</div></div></div>
        <div id="recent" class="recent-list"><div class="card-hint">No recent sites yet.</div></div>
      </section>
    </div>

    <div class="column">
      <section class="card glass fade">
        <div class="card-head"><div><div class="card-title">Automation</div><div class="card-hint">Human-first by default</div></div></div>
        <div class="automation">
          <div class="status-line"><div id="statusOrb" class="status-orb"></div><div id="statusMain" class="status-main">Ready</div></div>
          <div id="statusCopy" class="status-copy">No foreground automation is running.</div>
          <button id="stopAutomation" class="soft-btn" type="button">Stop automation</button>
        </div>
      </section>

      <section class="card glass fade">
        <div class="card-head"><div><div class="card-title">Quick note</div><div class="card-hint">Saved locally in the home origin</div></div></div>
        <textarea id="note" maxlength="4000" placeholder="Things to remember, a URL to revisit, what to do next…"></textarea>
      </section>

      <section class="card glass fade">
        <div class="card-title">Private by design</div>
        <p class="status-copy">The home dashboard does not load remote scripts, images or feeds. Favorites and notes stay in the WebView2 profile; recent origins come from Luma's local SQLite audit log.</p>
      </section>
    </div>
  </div>

  <div class="footer"><span>Luma · personal Windows browser</span><span>Local dashboard · no news feed · no background model</span></div>
</main>
<script>
(() => {
  const DEFAULT_FAVORITES = [
    { name: 'GitHub', url: 'https://github.com' },
    { name: 'ChatGPT', url: 'https://chatgpt.com' },
    { name: 'Naver', url: 'https://www.naver.com' },
    { name: 'YouTube', url: 'https://www.youtube.com' }
  ];
  const $ = id => document.getElementById(id);
  const post = action => window.chrome?.webview?.postMessage({ kind: 'luma-home', action });
  const cleanHost = url => { try { return new URL(url).host.replace(/^www\./, ''); } catch { return url; } };
  const initials = name => (name || '?').trim().slice(0,2).toUpperCase();

  function tick() {
    const now = new Date();
    const hour = now.getHours();
    $('greeting').textContent = hour < 12 ? 'Good morning.' : hour < 18 ? 'Good afternoon.' : 'Good evening.';
    $('time').textContent = new Intl.DateTimeFormat(undefined,{hour:'2-digit',minute:'2-digit'}).format(now);
    $('date').textContent = new Intl.DateTimeFormat(undefined,{weekday:'long',month:'long',day:'numeric'}).format(now);
  }
  tick(); setInterval(tick, 30000);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) tick(); });

  $('searchForm').addEventListener('submit', e => {
    e.preventDefault();
    const q = $('search').value.trim();
    if (!q) return;
    if (/^https?:\/\//i.test(q)) location.href = q;
    else if (!q.includes(' ') && q.includes('.')) location.href = 'https://' + q;
    else location.href = 'https://www.google.com/search?q=' + encodeURIComponent(q);
  });

  const checkedFavoriteUrl = raw => {
    if (typeof raw !== 'string' || !raw || /[\\\\\u0000-\u001f]/.test(raw)) return null;
    try {
      const url = new URL(/^https?:\\/\\//i.test(raw) ? raw : 'https://' + raw);
      if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) return null;
      return url.href;
    } catch { return null; }
  };
  function loadFavorites() {
    try {
      const stored = JSON.parse(localStorage.getItem('luma.home.favorites.v1'));
      const items = Array.isArray(stored) ? stored : DEFAULT_FAVORITES;
      return items.slice(0,12).flatMap(item => {
        const url = checkedFavoriteUrl(item?.url);
        return url && typeof item?.name === 'string'
          ? [{ name: item.name.trim().slice(0,40), url }]
          : [];
      });
    } catch { return DEFAULT_FAVORITES; }
  }
  function saveFavorites(items) { localStorage.setItem('luma.home.favorites.v1', JSON.stringify(items.slice(0,12))); }
  function renderFavorites() {
    const root = $('favorites'); root.textContent = '';
    loadFavorites().forEach(item => {
      const a = document.createElement('a'); a.className = 'favorite'; a.href = item.url;
      const icon = document.createElement('div'); icon.className = 'favicon'; icon.textContent = initials(item.name);
      const name = document.createElement('div'); name.className = 'favorite-name'; name.textContent = item.name;
      a.append(icon,name); root.append(a);
    });
  }
  renderFavorites();
  $('addFavorite').addEventListener('click', () => {
    const name = prompt('Favorite name'); if (!name || !name.trim()) return;
    const raw = prompt('HTTPS or HTTP address'); if (!raw) return;
    const url = checkedFavoriteUrl(raw);
    if (!url) { alert('Enter an HTTP(S) address without embedded credentials.'); return; }
    const items = loadFavorites(); items.push({name:name.trim().slice(0,40),url}); saveFavorites(items); renderFavorites();
  });

  const note = $('note'); note.value = localStorage.getItem('luma.home.note.v1') || '';
  note.addEventListener('input', () => localStorage.setItem('luma.home.note.v1', note.value));

  document.querySelectorAll('[data-action]').forEach(button => button.addEventListener('click', () => post(button.dataset.action)));
  $('stopAutomation').addEventListener('click', () => post('stop'));

  window.lumaHydrate = payload => {
    const recent = Array.isArray(payload?.recent) ? payload.recent : [];
    const root = $('recent'); root.textContent = '';
    if (!recent.length) root.innerHTML = '<div class="card-hint">No recent sites yet.</div>';
    recent.forEach(item => {
      const a = document.createElement('a'); a.className='recent'; a.href=item.url;
      const dot=document.createElement('div'); dot.className='recent-dot';
      const text=document.createElement('div'); text.className='recent-text';
      const title=document.createElement('div'); title.className='recent-title'; title.textContent=item.title || cleanHost(item.url);
      const url=document.createElement('div'); url.className='recent-url'; url.textContent=cleanHost(item.url);
      text.append(title,url); a.append(dot,text); root.append(a);
    });
    const s = payload?.state || {};
    const busy = !!(s.recording || s.watching || s.replaying);
    $('statusOrb').className = 'status-orb' + (busy ? ' busy' : '');
    $('statusMain').textContent = s.recording ? 'Recording macro' : s.replaying ? 'Replaying macro' : s.watching ? 'Foreground Watch active' : 'Ready';
    $('statusCopy').textContent = s.message || (busy ? 'Automation stays bound to the selected foreground tab.' : 'No foreground automation is running.');
  };
  post('hydrate');
})();
</script>
</body>
</html>
""";
}
