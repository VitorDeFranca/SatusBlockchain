import type { ApiResult } from '../api/client';
import type { Block, LabNodeResult, NodeCreated, NodeRole, StopMode, SyncReport, Transaction } from '../api/types';
import { plural, shortHash } from './network';

/** Uma linha do registro de operações. */
export interface Activity {
  ok: boolean;
  node: string;
  action: string;
  message: string;
  details?: string[];
}

/** Falhas comuns a qualquer operação: console fora, nó fora, recusa do nó. */
function failure(node: string, action: string, result: ApiResult<unknown>): Activity {
  const message =
    result.status === 0
      ? 'O console não respondeu (o gateway está no ar?).'
      : result.status === 502
        ? `Sem resposta: ${result.error}`
        : `Recusado (${result.status}): ${result.error}`;
  return { ok: false, node, action, message };
}

export function describeTransaction(node: string, transaction: Transaction, result: ApiResult<Transaction>): Activity {
  const action = 'Transação';
  if (!result.ok) return failure(node, action, result);
  return {
    ok: true,
    node,
    action,
    message: `${transaction.from} → ${transaction.to} (${transaction.amount}) entrou na mempool do ${node} e foi propagada aos peers.`,
  };
}

export function describeMine(node: string, result: ApiResult<Block>): Activity {
  const action = 'Minerar';
  if (!result.ok || !result.data) return failure(node, action, result);
  const block = result.data;
  return {
    ok: true,
    node,
    action,
    message: `Bloco #${block.index} minerado no ${node} com ${plural(block.transactions.length, 'transação', 'transações')} e enviado aos peers.`,
    details: [`hash ${shortHash(block.hash)}`, `nonce ${block.nonce}`, `anterior ${shortHash(block.previousHash)}`],
  };
}

/**
 * O relatório do sync em português. `nameOf` troca a URL do peer pelo id que a tela mostra
 * (o relatório do nó fala em endereços: http://node1:8080).
 */
export function describeSync(node: string, result: ApiResult<SyncReport>, nameOf: (url: string) => string): Activity {
  const action = 'Sincronizar';
  if (!result.ok || !result.data) return failure(node, action, result);
  const report = result.data;

  const message = report.adopted
    ? `${node} adotou a cadeia de ${nameOf(report.adopted.from)} (${report.previousLength} → ${report.length} blocos)` +
      (report.orphansBackToMempool > 0
        ? `; ${plural(report.orphansBackToMempool, 'transação órfã voltou', 'transações órfãs voltaram')} para a mempool.`
        : '.')
    : `${node} manteve a própria cadeia (${report.length} blocos): nenhum peer tinha uma válida mais longa.`;

  const outcome: Record<string, string> = {
    Adopted: 'adotada',
    Kept: 'mantida a local',
    Invalid: 'recusada',
    Unreachable: 'sem resposta',
  };

  return {
    ok: true,
    node,
    action,
    message,
    details: report.peers.map((peer) => `${nameOf(peer.peer)}: ${outcome[peer.outcome] ?? peer.outcome} — ${peer.detail}`),
  };
}

export function describeCreate(result: ApiResult<NodeCreated>): Activity {
  const action = 'Adicionar nó';
  if (!result.ok || !result.data) return failure('rede', action, result);
  const created = result.data;
  return {
    ok: true,
    node: created.id,
    action,
    message:
      `${created.id} criado: entra pela semente ${created.seed} (dificuldade ${created.difficulty}), ` +
      'descobre os outros nós e sincroniza a cadeia sozinho.' +
      (created.hostPort ? ` Do host: localhost:${created.hostPort}.` : ''),
  };
}

export function describeStop(node: string, mode: StopMode, result: ApiResult<unknown>): Activity {
  const action = mode === 'graceful' ? 'Parar' : 'Derrubar';
  if (!result.ok) return failure(node, action, result);
  return {
    ok: true,
    node,
    action,
    message:
      mode === 'graceful'
        ? `${node} parado com despedida (docker stop): nós que se anunciam avisam os peers e saem das listas na hora.`
        : `${node} derrubado sem aviso (docker kill): os peers o tiram da lista após 3 chamadas sem resposta — sementes nunca saem.`,
  };
}

export function describeStart(node: string, role: NodeRole, result: ApiResult<unknown>): Activity {
  const action = 'Religar';
  if (!result.ok) return failure(node, action, result);
  return {
    ok: true,
    node,
    action,
    message:
      role === 'seed'
        ? `${node} religado. É semente: volta só com o genesis — clique em Sincronizar para recuperar a cadeia.`
        : `${node} religado: ele se reanuncia aos peers e sincroniza a cadeia sozinho.`,
  };
}

export function describeRemove(node: string, result: ApiResult<unknown>): Activity {
  const action = 'Remover';
  if (!result.ok) return failure(node, action, result);
  return { ok: true, node, action, message: `${node} se despediu dos peers e o container foi apagado.` };
}

// ---- laboratório (etapa 12) ----

/** Nós que não aceitaram a ação (status diferente de 2xx), para os detalhes. */
function failedNodes(results: LabNodeResult[]): string[] {
  return results
    .filter((result) => result.status < 200 || result.status >= 300)
    .map((result) => `${result.node}: não aplicado (${result.status})`);
}

export function describePartition(groups: Record<string, string>, result: ApiResult<LabNodeResult[]>): Activity {
  const action = 'Partição';
  if (!result.ok || !result.data) return failure('rede', action, result);

  const entries = Object.entries(groups);
  const sides = new Map<string, string[]>();
  for (const [id, group] of entries) sides.set(group, [...(sides.get(group) ?? []), id]);

  return {
    ok: true,
    node: 'rede',
    action,
    message:
      entries.length === 0
        ? 'Partição curada: todos os nós voltam a se enxergar. Os forks continuam até alguém sincronizar.'
        : `Partição aplicada — ${[...sides].map(([group, ids]) => `lado ${group}: ${ids.join(', ')}`).join(' · ')}. ` +
          'Nós de lados diferentes recusam o tráfego entre si (503); o console continua operando os dois.',
    details: failedNodes(result.data),
  };
}

export function describeReset(result: ApiResult<LabNodeResult[]>): Activity {
  const action = 'Remover falhas';
  if (!result.ok || !result.data) return failure('rede', action, result);
  return {
    ok: true,
    node: 'rede',
    action,
    message: `Falhas removidas de ${plural(result.data.length, 'nó', 'nós')}: sem partição e sem lentidão.`,
    details: failedNodes(result.data),
  };
}

/** O relatório de cada nó no "sincronizar todos", com URLs trocadas por ids. */
export function describeSyncAll(result: ApiResult<LabNodeResult[]>, nameOf: (url: string) => string): Activity {
  const action = 'Sincronizar todos';
  if (!result.ok || !result.data) return failure('rede', action, result);

  const reports = result.data.map((entry) => ({ node: entry.node, status: entry.status, report: entry.body as SyncReport | null }));
  const adopted = reports.filter((r) => r.report?.adopted);
  const orphans = reports.reduce((sum, r) => sum + (r.report?.orphansBackToMempool ?? 0), 0);

  return {
    ok: true,
    node: 'rede',
    action,
    message:
      adopted.length === 0
        ? 'Todos já estavam na cadeia válida mais longa que conseguem ver.'
        : `${plural(adopted.length, 'nó adotou', 'nós adotaram')} a cadeia mais longa` +
          (orphans > 0 ? `; ${plural(orphans, 'transação órfã voltou', 'transações órfãs voltaram')} para a mempool.` : '.'),
    details: reports.map(({ node, status, report }) =>
      !report || status < 200 || status >= 300
        ? `${node}: falhou (${status})`
        : report.adopted
          ? `${node}: adotou a cadeia de ${nameOf(report.adopted.from)} (${report.previousLength} → ${report.length} blocos)`
          : `${node}: manteve a própria (${report.length} blocos)`,
    ),
  };
}

export function describeDelay(node: string, ms: number, result: ApiResult<unknown>): Activity {
  const action = 'Lentidão';
  if (!result.ok) return failure(node, action, result);
  return {
    ok: true,
    node,
    action,
    message:
      ms === 0
        ? `${node} voltou a responder aos peers sem atraso.`
        : `${node} agora atrasa ${ms} ms cada resposta aos peers` +
          (ms >= 2000 ? ' — acima do timeout de 2 s: para os peers, é como se tivesse caído.' : '.'),
  };
}

/** Bloco entregue a um nó como um peer faria. No laboratório, a recusa é o resultado esperado. */
export function describeDelivery(node: string, block: Block, result: ApiResult<unknown>): Activity {
  const action = 'Entregar bloco';
  if (result.ok) return { ok: true, node, action, message: `${node} aceitou o bloco #${block.index}.` };
  if (result.status === 409)
    return {
      ok: false,
      node,
      action,
      message: `${node} recusou o bloco #${block.index} (409): ${result.error} A validação de cada nó é a defesa contra um peer adulterador.`,
    };
  return failure(node, action, result);
}
