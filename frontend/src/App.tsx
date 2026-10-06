import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import TerminalView from './components/TerminalView';
import { MakeTarget, Session, postToHost } from './types';

type HostMsg =
  | { type: 'targets'; file: string; targets: MakeTarget[] }
  | { type: 'session-started'; sessionId: string; target: string }
  | { type: 'output'; sessionId: string; data: string }
  | { type: 'exit'; sessionId: string; code: number }
  | { type: 'error'; message: string }
  | { type: 'assoc'; registered: boolean };

export default function App() {
  const [file, setFile] = useState<string>('');
  const [targets, setTargets] = useState<MakeTarget[]>([]);
  const [query, setQuery] = useState('');
  const [sessions, setSessions] = useState<Session[]>([]);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [assoc, setAssoc] = useState<boolean | null>(null);
  const [noHost, setNoHost] = useState(false);
  const gotMsg = useRef(false);

  const buffers = useRef(new Map<string, string[]>());
  const listeners = useRef(new Map<string, Set<(d: string) => void>>());

  const showToast = (m: string) => {
    setToast(m);
    setTimeout(() => setToast(null), 5000);
  };

  useEffect(() => {
    const handler = (e: MessageEvent) => {
      const msg = e.data as HostMsg;
      if (!msg || typeof msg !== 'object' || !('type' in msg)) return;
      gotMsg.current = true;
      switch (msg.type) {
        case 'targets':
          setFile(msg.file);
          setTargets(msg.targets);
          buffers.current.clear();
          break;
        case 'session-started': {
          const s: Session = { id: msg.sessionId, target: msg.target, status: 'running', exitCode: null, chunks: [] };
          buffers.current.set(s.id, []);
          setSessions((prev) => [...prev, s]);
          setActiveId(s.id);
          break;
        }
        case 'output': {
          const set = listeners.current.get(msg.sessionId);
          if (set && set.size > 0) set.forEach((fn) => fn(msg.data));
          else buffers.current.get(msg.sessionId)?.push(msg.data);
          break;
        }
        case 'exit':
          setSessions((prev) =>
            prev.map((s) => (s.id === msg.sessionId ? { ...s, status: 'done', exitCode: msg.code } : s)),
          );
          break;
        case 'error':
          showToast(msg.message);
          break;
        case 'assoc':
          setAssoc(msg.registered);
          break;
      }
    };
    const w = window as unknown as { chrome?: { webview?: { addEventListener: (t: string, f: (e: MessageEvent) => void) => void } } };
    w.chrome?.webview?.addEventListener('message', handler);
    postToHost({ type: 'ready' });
    postToHost({ type: 'assocStatus' });
    // Nếu sau 2s host không trả lời gì -> hiện cảnh báo (mở ngoài app / WebView lỗi)
    setTimeout(() => { if (!gotMsg.current) setNoHost(true); }, 2000);
    const onErr = (ev: ErrorEvent) => showToast(`Lỗi web: ${ev.message}`);
    window.addEventListener('error', onErr);
    return () => window.removeEventListener('error', onErr);
  }, []);

  const drain = useCallback(
    (id: string) => () => {
      const b = buffers.current.get(id) ?? [];
      buffers.current.set(id, []);
      return b;
    },
    [],
  );

  const subscribe = useCallback(
    (id: string) => (fn: (d: string) => void) => {
      let set = listeners.current.get(id);
      if (!set) { set = new Set(); listeners.current.set(id, set); }
      set.add(fn);
      return () => { listeners.current.get(id)?.delete(fn); };
    },
    [],
  );

  const run = (name: string) => postToHost({ type: 'run', target: name });
  const kill = (id: string) => {
    postToHost({ type: 'kill', sessionId: id });
    setSessions((prev) => prev.map((s) => (s.id === id ? { ...s, status: 'killed' as const } : s)));
  };
  const closeTab = (id: string) => {
    const s = sessions.find((x) => x.id === id);
    if (s?.status === 'running') postToHost({ type: 'kill', sessionId: id });
    setSessions((prev) => prev.filter((x) => x.id !== id));
    listeners.current.delete(id);
    buffers.current.delete(id);
    if (activeId === id) setActiveId(sessions.filter((x) => x.id !== id).slice(-1)[0]?.id ?? null);
  };

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return targets;
    return targets.filter(
      (t) =>
        t.name.toLowerCase().includes(q) ||
        t.desc.toLowerCase().includes(q) ||
        t.commands.some((c) => c.toLowerCase().includes(q)),
    );
  }, [targets, query]);

  const active = sessions.find((s) => s.id === activeId) ?? null;

  return (
    <div className="flex h-full flex-col bg-slate-100 text-slate-800">
      {/* Title bar */}
      <header className="flex items-center gap-3 border-b border-slate-200 bg-white px-4 py-2.5 shadow-sm">
        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-gradient-to-br from-cyan-400 to-violet-500 text-lg font-bold text-white">⚡</div>
        <div className="min-w-0">
          <div className="text-sm font-semibold tracking-wide text-slate-800">Makelazy <span className="text-cyan-600">· Makefile Runner</span></div>
          <div className="truncate text-xs text-slate-500" title={file}>{file || 'Chưa mở Makefile — bấm “Mở Makefile” hoặc kéo-thả file vào cửa sổ'}</div>
        </div>
        <div className="ml-auto flex items-center gap-2">
          <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-xs text-slate-600">{targets.length} targets</span>
          <button
            onClick={() => postToHost({ type: assoc ? 'unregisterAssoc' : 'registerAssoc' })}
            title={assoc ? 'Đã ghim: double-click Makefile sẽ mở bằng app. Bấm để gỡ.' : 'Ghim app vào Open-With của file Makefile (double-click mở bằng app)'}
            className={`rounded-lg px-3 py-1.5 text-sm font-semibold shadow-sm active:scale-95 ${
              assoc ? 'bg-emerald-500 text-white hover:bg-emerald-600' : 'border border-slate-200 bg-white text-slate-600 hover:border-cyan-500 hover:text-cyan-700'
            }`}
          >{assoc ? '✔ Đã ghim Open-With' : '📌 Ghim Open-With'}</button>
          <button
            onClick={() => postToHost({ type: 'openMakefile' })}
            className="rounded-lg bg-cyan-600 px-3.5 py-1.5 text-sm font-semibold text-white shadow hover:bg-cyan-500 active:scale-95"
          >📂 Mở Makefile</button>
        </div>
      </header>

      {noHost && (
        <div className="border-b border-amber-300 bg-amber-50 px-4 py-1.5 text-xs text-amber-800">
          ⚠ Không kết nối được backend (mở trang ngoài app Makelazy?). Các nút ▶ sẽ không chạy — hãy mở bằng file <b>Makelazy.exe</b>.
        </div>
      )}

      <div className="flex min-h-0 flex-1">
        {/* LEFT: targets = nút bấm */}
        <aside className="flex w-[340px] min-w-[260px] max-w-[480px] resize-x flex-col overflow-hidden border-r border-slate-200 bg-white">
          <div className="p-3">
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="🔍 Tìm target / lệnh…"
              className="w-full rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-800 outline-none placeholder:text-slate-400 focus:border-cyan-500 focus:bg-white"
            />
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto px-3 pb-3">
            {filtered.length === 0 && (
              <div className="mt-10 text-center text-sm text-slate-400">
                {targets.length === 0 ? (
                  <><div className="text-4xl">📄</div><p className="mt-2">Mở file <b>Makefile</b> (không đuôi) để hiện các lệnh.</p></>
                ) : 'Không tìm thấy target nào.'}
              </div>
            )}
            {filtered.map((t) => (
              <div key={t.name} className="mb-2 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm hover:border-cyan-400">
                <div className="flex items-center gap-2 p-2.5">
                  <button
                    onClick={() => run(t.name)}
                    title={`Chạy: make ${t.name}`}
                    className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-emerald-400 to-cyan-500 text-base text-white shadow hover:brightness-110 active:scale-90"
                  >▶</button>
                  <button onClick={() => setExpanded(expanded === t.name ? null : t.name)} className="min-w-0 flex-1 text-left">
                    <div className="flex items-center gap-1.5">
                      <code className="truncate text-sm font-bold text-cyan-700">{t.name}</code>
                      {t.phony && <span className="rounded bg-violet-100 px-1.5 text-[10px] font-semibold text-violet-600">PHONY</span>}
                      <span className="text-[10px] text-slate-400">:{t.line}</span>
                    </div>
                    {t.desc && <div className="truncate text-xs text-slate-500">{t.desc}</div>}
                    {t.deps.length > 0 && <div className="truncate text-[11px] text-slate-400">deps: {t.deps.join(' ')}</div>}
                  </button>
                </div>
                {expanded === t.name && (
                  <div className="border-t border-slate-100 bg-slate-50 p-2.5">
                    {t.commands.length === 0 && <div className="text-xs italic text-slate-400">(không có recipe)</div>}
                    {t.commands.map((c, i) => (
                      <pre key={i} className="mb-1 overflow-x-auto rounded bg-white px-2 py-1 font-mono text-[11px] text-slate-700 shadow-sm ring-1 ring-slate-200">$ {c}</pre>
                    ))}
                  </div>
                )}
              </div>
            ))}
          </div>
        </aside>

        {/* RIGHT: tab terminal */}
        <main className="flex min-w-0 flex-1 flex-col bg-slate-100">
          <div className="flex items-center gap-1 overflow-x-auto border-b border-slate-200 bg-white px-2 py-1.5">
            {sessions.length === 0 && <span className="px-2 text-xs text-slate-400">Chưa có phiên chạy — bấm ▶ ở target bên trái.</span>}
            {sessions.map((s) => (
              <div
                key={s.id}
                onClick={() => setActiveId(s.id)}
                className={`flex cursor-pointer items-center gap-2 whitespace-nowrap rounded-t-lg border-b-2 px-3 py-1.5 text-xs ${
                  s.id === activeId ? 'border-cyan-500 bg-slate-50 font-semibold text-slate-800' : 'border-transparent text-slate-500 hover:text-slate-700'
                }`}
              >
                <span className={`h-2 w-2 rounded-full ${s.status === 'running' ? 'animate-pulse bg-emerald-500' : s.exitCode === 0 ? 'bg-slate-300' : 'bg-red-400'}`} />
                <span className="font-mono">{s.target}</span>
                <span className="text-slate-400">#{s.id}</span>
                {s.status === 'running' ? (
                  <button onClick={(e) => { e.stopPropagation(); kill(s.id); }} title="Kill" className="rounded px-1 hover:bg-red-100 hover:text-red-500">■</button>
                ) : (
                  <span className="text-slate-400">({s.exitCode})</span>
                )}
                <button onClick={(e) => { e.stopPropagation(); closeTab(s.id); }} title="Đóng tab" className="rounded px-1 hover:bg-slate-200">✕</button>
              </div>
            ))}
            {active?.status === 'running' && (
              <button onClick={() => kill(active.id)} className="ml-auto mr-1 rounded-lg border border-red-300 bg-red-50 px-2.5 py-1 text-xs font-semibold text-red-500 hover:bg-red-100">■ Stop</button>
            )}
          </div>
          <div className="relative min-h-0 flex-1 p-3">
            {sessions.length === 0 ? (
              <div className="flex h-full items-center justify-center rounded-xl border border-dashed border-slate-300 bg-white">
                <div className="text-center text-slate-400">
                  <div className="text-5xl">🖥️</div>
                  <p className="mt-3 text-sm">Output stream của process sẽ render ở đây bằng terminal (xterm.js).</p>
                  <p className="mt-1 text-xs">Mỗi lần bấm ▶ là 1 tab terminal riêng, stream trực tiếp, gõ được stdin.</p>
                </div>
              </div>
            ) : (
              <div className="relative h-full">
                {sessions.map((s) => (
                  <div
                    key={s.id}
                    className={`absolute inset-0 ${s.id === activeId ? 'visible z-10' : 'invisible z-0'}`}
                  >
                    <div className="h-full overflow-hidden rounded-xl border border-slate-200 bg-white p-2 shadow-sm">
                      <TerminalView sessionId={s.id} active={s.id === activeId} drain={drain(s.id)} subscribe={subscribe(s.id)} />
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
          <footer className="flex items-center gap-2 border-t border-slate-200 bg-white px-3 py-1 text-[11px] text-slate-400">
            <span>Ẩn terminal thật (CreateNoWindow) · stream stdout/stderr trực tiếp · stdin gõ trong terminal</span>
            {active && <span className="ml-auto font-mono">session #{active.id} · {active.status}{active.exitCode !== null && ` · exit ${active.exitCode}`}</span>}
          </footer>
        </main>
      </div>

      {toast && (
        <div className="fixed bottom-6 left-1/2 max-w-[80%] -translate-x-1/2 rounded-lg border border-red-300 bg-red-50 px-4 py-2 text-sm text-red-600 shadow-xl">{toast}</div>
      )}
    </div>
  );
}
