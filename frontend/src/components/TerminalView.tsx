import { useEffect, useRef } from 'react';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { WebLinksAddon } from '@xterm/addon-web-links';
import { postToHost } from '../types';

interface Props {
  sessionId: string;
  active: boolean;
  drain: () => string[];
  subscribe: (fn: (data: string) => void) => () => void;
}

export default function TerminalView({ sessionId, active, drain, subscribe }: Props) {
  const divRef = useRef<HTMLDivElement>(null);
  const termRef = useRef<Terminal | null>(null);
  const fitRef = useRef<FitAddon | null>(null);

  useEffect(() => {
    const term = new Terminal({
      cursorBlink: true,
      fontSize: 13,
      fontFamily: 'Cascadia Code, Consolas, monospace',
      theme: {
        background: '#ffffff',
        foreground: '#1f2328',
        cursor: '#0969da',
        cursorAccent: '#ffffff',
        selectionBackground: '#b6e3ff',
        black: '#24292f',
        red: '#cf222e',
        green: '#116329',
        yellow: '#4d2d00',
        blue: '#0969da',
        magenta: '#8250df',
        cyan: '#1b7c83',
        white: '#6e7781',
        brightBlack: '#57606a',
        brightRed: '#a40e26',
        brightGreen: '#1a7f37',
        brightYellow: '#9a6700',
        brightBlue: '#218bff',
        brightMagenta: '#a475f9',
        brightCyan: '#3192aa',
        brightWhite: '#8c959f',
      },
    });
    const fit = new FitAddon();
    term.loadAddon(fit);
    term.loadAddon(new WebLinksAddon());
    try {
      term.open(divRef.current!);
    } catch {
      // parent chưa có kích thước (tab ẩn): không crash app, data giữ trong buffer, mount sau sẽ flush
      term.dispose();
      return;
    }
    for (const c of drain()) term.write(c);
    const unsub = subscribe((data) => {
      try { term.write(data); } catch { /* noop */ }
    });
    term.onData((d) => postToHost({ type: 'input', sessionId, data: d }));

    termRef.current = term;
    fitRef.current = fit;
    const ro = new ResizeObserver(() => { try { fit.fit(); } catch { /* noop */ } });
    if (divRef.current) ro.observe(divRef.current);
    setTimeout(() => { try { fit.fit(); } catch { /* noop */ } }, 50);

    return () => { unsub(); ro.disconnect(); term.dispose(); termRef.current = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionId]);

  useEffect(() => {
    if (active) setTimeout(() => { try { fitRef.current?.fit(); } catch { /* noop */ } }, 30);
  }, [active]);

  return <div ref={divRef} className="h-full w-full" />;
}
