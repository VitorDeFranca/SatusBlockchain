import type { NetworkSnapshot, NodeView } from '../api/types';

/** Um nó "em dia" com o mínimo preenchido; cada teste muda só o que importa para ele. */
export function nodeView(id: string, overrides: Partial<NodeView> = {}): NodeView {
  return {
    id,
    url: `http://${id}:8080`,
    hostPort: null,
    state: 'running',
    status: 'ok',
    role: 'seed',
    difficulty: 2,
    genesis: 'genesis',
    chain: { length: 3, latestHash: 'abc', valid: true },
    mempool: 0,
    advertiseUrl: null,
    peers: [],
    latencyMs: 5,
    lab: { partition: null, delayMs: 0 },
    ...overrides,
  };
}

/** Peer referenciando outro nó do snapshot pelo id (é assim que o gateway devolve). */
export const peer = (id: string) => ({ url: `http://${id}:8080`, nodeId: id });

export function snapshotOf(nodes: NodeView[], summary: Partial<NetworkSnapshot['summary']> = {}): NetworkSnapshot {
  const running = nodes.filter((node) => node.state === 'running');
  return {
    generatedAt: '2026-10-18T12:00:00Z',
    mode: 'docker',
    summary: {
      total: nodes.length,
      reachable: running.length,
      maxLength: Math.max(0, ...running.map((node) => node.chain?.length ?? 0)),
      converged: running.length > 0 && running.every((node) => node.status === 'ok'),
      ...summary,
    },
    nodes,
  };
}
