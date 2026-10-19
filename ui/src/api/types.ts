// Contrato da API do gateway (src/SatusBlockchain.Gateway). Os enums chegam como texto
// camelCase ("behind", "unreachable") — o JsonStringEnumConverter do Program.cs.

export type NodeState = 'running' | 'unreachable' | 'stopped';

export type NodeStatus = 'ok' | 'behind' | 'diverged' | 'invalid' | 'incompatible' | 'unknown';

export type NodeRole = 'seed' | 'dynamic';

export interface PeerRef {
  url: string;
  /** Preenchido quando o console conhece esse peer (é por ele que as arestas são desenhadas). */
  nodeId: string | null;
}

export interface ChainView {
  length: number;
  latestHash: string;
  valid: boolean;
}

/** Falhas injetadas pelo laboratório (etapa 12). */
export interface LabView {
  /** Grupo de partição; null = sem partição. */
  partition: string | null;
  /** Atraso nas respostas aos peers (ms). */
  delayMs: number;
}

export interface NodeView {
  id: string;
  url: string;
  hostPort: number | null;
  state: NodeState;
  status: NodeStatus;
  role: NodeRole;
  difficulty: number | null;
  genesis: string | null;
  chain: ChainView | null;
  mempool: number | null;
  advertiseUrl: string | null;
  peers: PeerRef[];
  latencyMs: number | null;
  /** Null quando o nó não respondeu. */
  lab: LabView | null;
}

export interface NetworkSummary {
  total: number;
  reachable: number;
  maxLength: number;
  converged: boolean;
}

export interface NetworkSnapshot {
  generatedAt: string;
  mode: 'docker' | 'static';
  summary: NetworkSummary;
  nodes: NodeView[];
}

export interface Capabilities {
  runtime: string;
  canManageNodes: boolean;
  links: { grafana: string; jaeger: string; prometheus: string };
}

// ---- formatos da API dos NÓS, repassados intactos pelo gateway ----

export interface Transaction {
  from: string;
  to: string;
  amount: number;
}

export interface Block {
  index: number;
  timestamp: string;
  transactions: Transaction[];
  previousHash: string;
  nonce: number;
  hash: string;
}

export interface SyncReport {
  node: string;
  previousLength: number;
  length: number;
  adopted: { from: string; length: number } | null;
  orphansBackToMempool: number;
  peers: { peer: string; outcome: string; detail: string }[];
}

export interface NodeCreated {
  id: string;
  url: string;
  hostPort: number | null;
  seed: string;
  difficulty: number;
}

export type StopMode = 'graceful' | 'crash';

/** O que o nó faria com um bloco, sem anexá-lo (POST /blocks/inspect, etapa 12). */
export interface BlockInspection {
  recalculatedHash: string;
  hashMatches: boolean;
  proofOfWork: boolean;
  inChain: boolean;
  extendsTip: boolean;
  wouldAccept: boolean;
  verdict: string;
}

/** Resposta de UM nó a uma ação do laboratório sobre a rede inteira. */
export interface LabNodeResult {
  node: string;
  status: number;
  body: unknown;
}
