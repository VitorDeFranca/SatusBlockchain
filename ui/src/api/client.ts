import type {
  Block,
  BlockInspection,
  Capabilities,
  LabNodeResult,
  NetworkSnapshot,
  NodeCreated,
  StopMode,
  SyncReport,
  Transaction,
} from './types';

/**
 * Resultado de uma operação: nunca lança. A tela precisa do status e da mensagem do nó
 * (o 409 "a cadeia mudou durante a mineração" é informação, não exceção). `status` 0 = o
 * próprio console (gateway) não respondeu.
 */
export interface ApiResult<T> {
  ok: boolean;
  status: number;
  data: T | null;
  error: string | null;
}

async function call<T>(method: string, path: string, body?: unknown): Promise<ApiResult<T>> {
  let response: Response;
  try {
    response = await fetch(path, {
      method,
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    return { ok: false, status: 0, data: null, error: 'O console não respondeu (gateway fora do ar?).' };
  }

  const text = await response.text();
  let json: unknown = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    // Corpo que não é JSON: fica só o status.
  }

  if (response.ok) return { ok: true, status: response.status, data: json as T, error: null };

  return { ok: false, status: response.status, data: null, error: errorOf(json) ?? `HTTP ${response.status}` };
}

/** Nós e gateway respondem erros como `{ "error": "..." }`; o ASP.NET, às vezes, como ProblemDetails. */
function errorOf(json: unknown): string | null {
  if (json && typeof json === 'object') {
    const record = json as Record<string, unknown>;
    if (typeof record.error === 'string') return record.error;
    if (typeof record.title === 'string') return record.title;
  }
  return null;
}

/** Leituras (React Query): falha vira exceção, que o React Query transforma em estado de erro. */
async function read<T>(path: string): Promise<T> {
  const result = await call<T>('GET', path);
  if (!result.ok || result.data === null) throw new Error(result.error ?? `HTTP ${result.status}`);
  return result.data;
}

const node = (id: string) => `/api/nodes/${encodeURIComponent(id)}`;

export const api = {
  network: () => read<NetworkSnapshot>('/api/network'),
  capabilities: () => read<Capabilities>('/api/capabilities'),
  lastBlocks: (id: string, last = 5) => read<Block[]>(`${node(id)}/chain?last=${last}`),
  pending: (id: string) => read<Transaction[]>(`${node(id)}/pending`),

  postTransaction: (id: string, transaction: Transaction) =>
    call<Transaction>('POST', `${node(id)}/transactions`, transaction),
  mine: (id: string) => call<Block>('POST', `${node(id)}/mine`),
  sync: (id: string) => call<SyncReport>('POST', `${node(id)}/sync`),

  createNode: (hostPort?: number) => call<NodeCreated>('POST', '/api/nodes', hostPort ? { hostPort } : {}),
  stopNode: (id: string, mode: StopMode) => call<unknown>('POST', `${node(id)}/stop?mode=${mode}`),
  startNode: (id: string) => call<unknown>('POST', `${node(id)}/start`),
  removeNode: (id: string) => call<unknown>('DELETE', node(id)),

  // ---- explorador e laboratório (etapa 12) ----
  chain: (id: string) => read<Block[]>(`${node(id)}/chain`),
  inspect: (id: string, block: Block) => call<BlockInspection>('POST', `${node(id)}/blocks/inspect`, block),
  deliverBlock: (id: string, block: Block) => call<unknown>('POST', `${node(id)}/blocks/receive`, block),
  setDelay: (id: string, ms: number) => call<unknown>('PUT', `${node(id)}/lab/delay`, { ms }),
  applyPartition: (groups: Record<string, string>) => call<LabNodeResult[]>('PUT', '/api/lab/partition', { groups }),
  resetLab: () => call<LabNodeResult[]>('DELETE', '/api/lab'),
  syncAll: () => call<LabNodeResult[]>('POST', '/api/lab/sync-all'),
};
