import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { api } from '../api/client';
import type { Block, NetworkSnapshot } from '../api/types';
import { compareChains, describeComparison, linkOk } from '../domain/chain';
import { BlockDetail } from './BlockDetail';
import { Hash } from './Hash';

interface Props {
  snapshot: NetworkSnapshot;
  initialNodeId: string | null;
  initialCompareId: string | null;
  onTamper: (owner: string, index: number) => void;
}

interface Selection {
  owner: string;
  index: number;
}

/**
 * Explorador da cadeia (etapa 12): os blocos de um nó, encadeados, com os zeros do PoW em
 * destaque e o elo de cada um conferido — e a comparação com outro nó, alinhada por índice,
 * que mostra onde está o ponto de fork.
 */
export function ChainExplorer({ snapshot, initialNodeId, initialCompareId, onTamper }: Props) {
  const running = snapshot.nodes.filter((node) => node.state === 'running');
  const [nodeId, setNodeId] = useState(initialNodeId ?? running[0]?.id ?? '');
  const [compareId, setCompareId] = useState(initialCompareId ?? '');
  const [selected, setSelected] = useState<Selection | null>(null);

  const chainA = useQuery({ queryKey: ['chain', nodeId], queryFn: () => api.chain(nodeId), enabled: !!nodeId, refetchInterval: 4000 });
  const chainB = useQuery({
    queryKey: ['chain', compareId],
    queryFn: () => api.chain(compareId),
    enabled: !!compareId,
    refetchInterval: 4000,
  });

  const difficultyOf = (id: string) => snapshot.nodes.find((node) => node.id === id)?.difficulty ?? null;
  const chainOf = (id: string) => (id === nodeId ? chainA.data : id === compareId ? chainB.data : undefined);
  const selectedChain = selected ? chainOf(selected.owner) : undefined;
  const selectedBlock = selected && selectedChain ? selectedChain[selected.index] : undefined;

  if (running.length === 0) return <p className="empty">Nenhum nó respondendo para explorar.</p>;

  return (
    <div className="explorer">
      <div className="explorer__controls">
        <label>
          Nó
          <select value={nodeId} onChange={(e) => { setNodeId(e.target.value); setSelected(null); }}>
            {running.map((node) => <option key={node.id} value={node.id}>{node.id}</option>)}
          </select>
        </label>
        <label>
          Comparar com
          <select value={compareId} onChange={(e) => { setCompareId(e.target.value); setSelected(null); }}>
            <option value="">— nenhum —</option>
            {running.filter((node) => node.id !== nodeId).map((node) => <option key={node.id} value={node.id}>{node.id}</option>)}
          </select>
        </label>
      </div>

      <div className="explorer__body">
        <section aria-label="Cadeia">
          {chainA.isError ? <p className="form-error">Não foi possível ler a cadeia do {nodeId}.</p> : null}
          {!chainA.data ? (
            <p className="muted">Carregando a cadeia…</p>
          ) : compareId && chainB.data ? (
            <Comparison
              a={chainA.data}
              b={chainB.data}
              nameA={nodeId}
              nameB={compareId}
              difficulty={difficultyOf(nodeId)}
              onSelect={setSelected}
            />
          ) : (
            <ChainList blocks={chainA.data} owner={nodeId} difficulty={difficultyOf(nodeId)} selected={selected} onSelect={setSelected} />
          )}
        </section>

        <aside className="explorer__detail" aria-label="Bloco selecionado">
          {selected && selectedBlock ? (
            <BlockDetail
              key={`${selected.owner}-${selectedBlock.hash}`}
              block={selectedBlock}
              owner={selected.owner}
              difficulty={difficultyOf(selected.owner)}
              previous={selectedChain?.[selected.index - 1]}
              onTamper={onTamper}
            />
          ) : (
            <p className="muted">Clique num bloco para ver as transações e pedir ao nó que o verifique.</p>
          )}
        </aside>
      </div>
    </div>
  );
}

interface ListProps {
  blocks: Block[];
  owner: string;
  difficulty: number | null;
  selected: Selection | null;
  onSelect: (selection: Selection) => void;
}

/** A cadeia do topo para o genesis: cada bloco aponta para o anterior pelo hash. */
function ChainList({ blocks, owner, difficulty, selected, onSelect }: ListProps) {
  return (
    <ol className="chain" reversed>
      {[...blocks].reverse().map((block) => {
        const ok = linkOk(block, blocks[block.index - 1]);
        const isSelected = selected?.owner === owner && selected.index === block.index;
        return (
          <li key={block.hash}>
            <button type="button" className={`block-card${isSelected ? ' block-card--selected' : ''}`}
              onClick={() => onSelect({ owner, index: block.index })}>
              <span className="block-card__index">#{block.index}</span>
              <Hash value={block.hash} difficulty={difficulty} />
              <span className="muted">
                {block.transactions.length} tx · nonce {block.nonce}
              </span>
              <span className={ok ? 'ok-text' : 'bad-text'}>
                {block.index === 0 ? 'genesis' : ok ? `↓ elo com o #${block.index - 1}` : '✕ elo quebrado'}
              </span>
            </button>
          </li>
        );
      })}
    </ol>
  );
}

interface ComparisonProps {
  a: Block[];
  b: Block[];
  nameA: string;
  nameB: string;
  difficulty: number | null;
  onSelect: (selection: Selection) => void;
}

/** As duas cadeias lado a lado, do genesis para o topo: onde param de coincidir é o fork. */
function Comparison({ a, b, nameA, nameB, difficulty, onSelect }: ComparisonProps) {
  const comparison = compareChains(a, b);
  const label = {
    same: 'em comum',
    fork: '⑂ ponto de fork',
    branch: 'ramos diferentes',
    onlyA: `só no ${nameA}`,
    onlyB: `só no ${nameB}`,
  } as const;

  const cell = (block: Block | null, owner: string) =>
    block ? (
      <button type="button" className="link-button" onClick={() => onSelect({ owner, index: block.index })}>
        <Hash value={block.hash} difficulty={difficulty} />
      </button>
    ) : (
      <span className="muted">—</span>
    );

  return (
    <div>
      <p className={comparison.forkIndex === null ? 'compare-summary' : 'compare-summary compare-summary--fork'}>
        {describeComparison(comparison, nameA, nameB, a.length, b.length)}
      </p>
      <table className="compare">
        <thead>
          <tr><th>#</th><th>{nameA}</th><th>{nameB}</th><th>situação</th></tr>
        </thead>
        <tbody>
          {comparison.rows.map((row) => (
            <tr key={row.index} className={`compare__row compare__row--${row.kind}`}>
              <td>{row.index}</td>
              <td>{cell(row.a, nameA)}</td>
              <td>{cell(row.b, nameB)}</td>
              <td>{label[row.kind]}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
