export interface MakeTarget {
  name: string;
  deps: string[];
  commands: string[];
  line: number;
  desc: string;
  phony: boolean;
}

export interface Session {
  id: string;
  target: string;
  status: 'running' | 'done' | 'killed';
  exitCode: number | null;
  chunks: string[]; // buffer output chưa flush vào xterm (khi tab chưa mount)
}

export function postToHost(msg: unknown) {
  const w = window as unknown as {
    chrome?: { webview?: { postMessage: (m: unknown) => void } };
  };
  w.chrome?.webview?.postMessage(msg);
}
