import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { api } from '../api/client';
import type { NetworkSnapshot, NodeView } from '../api/types';
import { shortHash, statusInfo } from '../domain/network';
import type { NodeActions } from '../hooks/useNodeActions';
import { TransactionForm } from './TransactionForm';

interface Props {
  node: NodeView;
  snapshot: NetworkSnapshot;
  canManage: boolean;
  actions: NodeActions;
  onSelect: (id: string) => void;
}

/** Tudo sobre o nó selecionado: quem é, o que ele vê, e o que dá para fazer com ele. */
export function NodePanel({ node, snapshot, canManage, actions, onSelect }: Props) {
  const info = statusInfo(node, snapshot.summary.maxLength);
  const running = node.state === 'running';
  const busy = actions.busy[node.id];
  const [confirmRemove, setConfirmRemove] = useState(false);

  const blocks = useQuery({
    queryKey: ['node', node.id, 'blocks'],
    queryFn: () => api.lastBlocks(node.id),
    enabled: running,
    refetchInterval: 4000,
  });
  const pending = useQuery({
    queryKey: ['node', node.id, 'pending'],
    queryFn: () => api.pending(node.id),
    enabled: running,
    refetchInterval: 4000,
  });

  const known = new Set(snapshot.nodes.map((n) => n.id));

  return (
    <div className="panel" aria-live="polite">
      <header className="panel__header">
        <h2>{node.id}</h2>
        <span className={`pill pill--${info.tone}`}>
          <span aria-hidden="true">{info.symbol}</span> {info.label}
        </span>
      </header>
      <p className="panel__description">{info.description}</p>

      <dl className="facts">
        <dt>Papel</dt>
        <dd>{node.role === 'seed' ? 'Semente (fixo no compose)' : 'Dinâmico (entrou em execução)'}</dd>
        <dt>Endereço</dt>
        <dd><code>{node.url}</code></dd>
        {node.hostPort ? (
          <>
            <dt>Do host</dt>
            <dd>
              <a href={`http://localhost:${node.hostPort}/`} target="_blank" rel="noreferrer">localhost:{node.hostPort}</a>
            </dd>
          </>
        ) : null}
        <dt>Cadeia</dt>
        <dd>{node.chain ? `${node.chain.length} blocos · topo ${shortHash(node.chain.latestHash)}` : '—'}</dd>
        <dt>Dificuldade</dt>
        <dd>{node.difficulty ?? '—'}</dd>
        <dt>Genesis</dt>
        <dd><code>{shortHash(node.genesis)}</code></dd>
        <dt>Latência</dt>
        <dd>{node.latencyMs !== null ? `${node.latencyMs} ms` : '—'}</dd>
      </dl>

      <section>
        <h3>Peers ({node.peers.length})</h3>
        {node.peers.length === 0 ? (
          <p className="muted">{running ? 'Nenhum peer na lista deste nó.' : 'Sem dados (nó não respondeu).'}</p>
        ) : (
          <ul className="chips">
            {node.peers.map((peer) =>
              peer.nodeId && known.has(peer.nodeId) ? (
                <li key={peer.url}>
                  <button type="button" className="chip" onClick={() => onSelect(peer.nodeId!)}>
                    {peer.nodeId}
                  </button>
                </li>
              ) : (
                <li key={peer.url} className="chip chip--unknown" title="Peer que o console não conhece">
                  {peer.url}
                </li>
              ),
            )}
          </ul>
        )}
      </section>

      <section>
        <h3>Operações</h3>
        {busy ? <p className="busy" role="status">{busy}</p> : null}
        <TransactionForm disabled={!running || !!busy} onSubmit={(tx) => actions.transaction(node.id, tx)} />
        <div className="buttons">
          <button type="button" onClick={() => actions.mine(node.id)} disabled={!running || !!busy}>
            Minerar bloco
          </button>
          <button type="button" onClick={() => actions.sync(node.id)} disabled={!running || !!busy}>
            Sincronizar
          </button>
        </div>
      </section>

      {canManage ? (
        <section>
          <h3>Ciclo de vida</h3>
          <div className="buttons">
            {node.state === 'stopped' ? (
              <button type="button" onClick={() => actions.start(node.id, node.role)} disabled={!!busy}>
                Religar
              </button>
            ) : (
              <>
                <button type="button" onClick={() => actions.stop(node.id, 'graceful')} disabled={!!busy}
                  title="docker stop: o nó se despede dos peers">
                  Parar (com despedida)
                </button>
                <button type="button" className="danger" onClick={() => actions.stop(node.id, 'crash')} disabled={!!busy}
                  title="docker kill: queda sem aviso">
                  Derrubar (queda)
                </button>
              </>
            )}
            {node.role === 'dynamic' ? (
              confirmRemove ? (
                <>
                  <button type="button" className="danger" disabled={!!busy}
                    onClick={() => { setConfirmRemove(false); actions.remove(node.id); }}>
                    Confirmar remoção
                  </button>
                  <button type="button" onClick={() => setConfirmRemove(false)}>Cancelar</button>
                </>
              ) : (
                <button type="button" className="danger" onClick={() => setConfirmRemove(true)} disabled={!!busy}>
                  Remover nó
                </button>
              )
            ) : null}
          </div>
        </section>
      ) : null}

      {running ? (
        <>
          <section>
            <h3>Mempool ({pending.data?.length ?? node.mempool ?? 0})</h3>
            {pending.data && pending.data.length > 0 ? (
              <ul className="list">
                {pending.data.slice(0, 6).map((tx, i) => (
                  <li key={`${tx.from}-${tx.to}-${tx.amount}-${i}`}>
                    {tx.from} → {tx.to} <strong>{tx.amount}</strong>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="muted">Nenhuma transação pendente.</p>
            )}
          </section>

          <section>
            <h3>Últimos blocos</h3>
            {blocks.data ? (
              <ol className="blocks" reversed>
                {[...blocks.data].reverse().map((block) => (
                  <li key={block.hash}>
                    <span className="blocks__index">#{block.index}</span>
                    <code title={block.hash}>{shortHash(block.hash)}</code>
                    <span className="muted">
                      ← {block.index === 0 ? 'genesis' : shortHash(block.previousHash)} · nonce {block.nonce} ·{' '}
                      {block.transactions.length} tx
                    </span>
                  </li>
                ))}
              </ol>
            ) : (
              <p className="muted">Carregando…</p>
            )}
          </section>
        </>
      ) : null}
    </div>
  );
}
