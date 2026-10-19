import type { NetworkSnapshot, NodeView } from '../api/types';

/** Família visual de um estado — cor E símbolo (nunca só a cor, por acessibilidade). */
export type Tone = 'ok' | 'warn' | 'fork' | 'down' | 'stopped' | 'bad';

export interface StatusInfo {
  label: string;
  symbol: string;
  tone: Tone;
  description: string;
}

/**
 * Como a tela descreve um nó. A classificação (ok, atrasado, fork...) vem pronta do gateway;
 * aqui ela só ganha nome, símbolo e uma frase que diz o que fazer.
 */
export function statusInfo(node: NodeView, maxLength: number): StatusInfo {
  if (node.state === 'stopped')
    return {
      label: 'Parado',
      symbol: '■',
      tone: 'stopped',
      description:
        node.role === 'seed'
          ? 'Container parado. Ao religar, a semente volta só com o genesis e precisa de Sincronizar.'
          : 'Container parado. Ao religar, o nó se reanuncia e sincroniza sozinho.',
    };

  if (node.state === 'unreachable')
    return {
      label: 'Fora do ar',
      symbol: '✕',
      tone: 'down',
      description: 'Não respondeu ao console: caiu, travou ou está isolado da rede.',
    };

  switch (node.status) {
    case 'ok':
      return { label: 'Em dia', symbol: '✓', tone: 'ok', description: 'Na cadeia líder da rede.' };
    case 'behind': {
      const missing = Math.max(0, maxLength - (node.chain?.length ?? 0));
      return {
        label: 'Atrasado',
        symbol: '↓',
        tone: 'warn',
        description: `${missing} ${missing === 1 ? 'bloco' : 'blocos'} atrás da cadeia líder. Sincronizar puxa a cadeia mais longa dos peers.`,
      };
    }
    case 'diverged':
      return {
        label: 'Fork',
        symbol: '⑂',
        tone: 'fork',
        description:
          'Tem blocos que não estão na cadeia líder: um ramo próprio. No sync, se a líder for mais longa, eles viram órfãos e as transações voltam para a mempool; no empate de tamanho, cada nó fica com o seu (regra "first seen") até alguém minerar o próximo bloco.',
      };
    case 'invalid':
      return { label: 'Cadeia inválida', symbol: '!', tone: 'bad', description: 'A própria cadeia não passa na validação (hash, elo ou PoW).' };
    case 'incompatible':
      return {
        label: 'Outra rede',
        symbol: '≠',
        tone: 'bad',
        description: 'Genesis diferente (outra dificuldade): nunca vai convergir com esta rede.',
      };
    default:
      return { label: 'Sem dados', symbol: '?', tone: 'stopped', description: 'O nó respondeu, mas sem dados de cadeia.' };
  }
}

/** Uma ligação entre dois nós, pelas listas de peers. */
export interface Edge {
  a: string;
  b: string;
  /** Os dois se listam. Falso = só `from` lista `to` (a visão da rede é local a cada nó). */
  mutual: boolean;
  from: string;
  to: string;
  /** Os dois estão em lados diferentes de uma partição simulada: a ligação existe, mas não passa nada. */
  cut: boolean;
}

/** Grupo de partição do nó (null = sem partição). */
export function partitionOf(node: NodeView): string | null {
  return node.lab?.partition ?? null;
}

/**
 * Arestas a partir das listas de peers: uma por par de nós conhecidos. A lista de cada nó é
 * a visão DELE — se A lista B e B não lista A, a aresta é de mão única (tracejada na tela).
 */
export function edgesOf(nodes: NodeView[]): Edge[] {
  const ids = new Set(nodes.map((node) => node.id));
  const partition = new Map(nodes.map((node) => [node.id, partitionOf(node)]));
  const directed = new Set<string>();

  for (const node of nodes)
    for (const peer of node.peers)
      if (peer.nodeId && peer.nodeId !== node.id && ids.has(peer.nodeId)) directed.add(`${node.id}\u0000${peer.nodeId}`);

  const edges = new Map<string, Edge>();
  for (const pair of directed) {
    const [from, to] = pair.split('\u0000');
    const [a, b] = from < to ? [from, to] : [to, from];
    const key = `${a}\u0000${b}`;
    if (edges.has(key)) continue;

    const mutual = directed.has(`${to}\u0000${from}`);
    edges.set(key, { a, b, mutual, from, to, cut: partition.get(a) !== partition.get(b) });
  }

  return [...edges.values()];
}

export interface Headline {
  text: string;
  tone: Tone;
}

/** A frase do topo: a rede de relance. */
export function headlineOf(snapshot: NetworkSnapshot): Headline {
  const { total, reachable, maxLength, converged } = snapshot.summary;

  if (total === 0) return { text: 'Nenhum nó encontrado', tone: 'stopped' };
  if (reachable === 0) return { text: 'Nenhum nó respondendo', tone: 'down' };

  if (converged)
    return { text: `Rede convergida · ${plural(reachable, 'nó', 'nós')} · ${plural(maxLength, 'bloco', 'blocos')}`, tone: 'ok' };

  const count = (status: string) => snapshot.nodes.filter((node) => node.state === 'running' && node.status === status).length;
  const parts = [
    [count('behind'), 'atrasado', 'atrasados'],
    [count('diverged'), 'em fork', 'em fork'],
    [count('invalid') + count('incompatible'), 'incompatível', 'incompatíveis'],
  ] as const;

  const details = parts.filter(([n]) => n > 0).map(([n, one, many]) => `${n} ${n === 1 ? one : many}`);
  return { text: `Rede divergente${details.length ? ': ' + details.join(', ') : ''}`, tone: 'warn' };
}

/** Quantos nós a plataforma conhece, mas não estão respondendo (fora do ar ou parados). */
export function offlineCount(snapshot: NetworkSnapshot): number {
  return snapshot.summary.total - snapshot.summary.reachable;
}

/** Nós cuja cadeia cresceu desde o snapshot anterior — o "pulso" da propagação na tela. */
export function grownNodes(previous: NetworkSnapshot | undefined, next: NetworkSnapshot): string[] {
  if (!previous) return [];
  const before = new Map(previous.nodes.map((node) => [node.id, node.chain?.length ?? 0]));
  return next.nodes
    .filter((node) => node.chain && before.has(node.id) && node.chain.length > (before.get(node.id) ?? 0))
    .map((node) => node.id);
}

/** Posições em círculo (até 32 nós cabem com folga num SVG de 640×440). */
export function circleLayout(count: number, width: number, height: number): { x: number; y: number }[] {
  if (count === 1) return [{ x: width / 2, y: height / 2 }];
  const radius = Math.min(width, height) / 2 - 70;
  return Array.from({ length: count }, (_, i) => {
    const angle = -Math.PI / 2 + (2 * Math.PI * i) / count;
    return { x: width / 2 + radius * Math.cos(angle), y: height / 2 + radius * Math.sin(angle) };
  });
}

/** Hash curto para leitura humana: o começo (onde estão os zeros do PoW) basta. */
export function shortHash(hash: string | null | undefined): string {
  if (!hash) return '—';
  return hash.length > 12 ? `${hash.slice(0, 12)}…` : hash;
}

export function plural(n: number, one: string, many: string): string {
  return `${n} ${n === 1 ? one : many}`;
}
