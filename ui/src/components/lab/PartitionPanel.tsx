import { useState } from 'react';
import type { NetworkSnapshot } from '../../api/types';
import { partitionOf } from '../../domain/network';
import type { NodeActions } from '../../hooks/useNodeActions';

type Side = '' | 'A' | 'B';

interface Props {
  snapshot: NetworkSnapshot;
  actions: NodeActions;
  busy: boolean;
}

/**
 * Partição livre: cada nó no lado A, no B ou fora da partição. Nós de lados diferentes não
 * trocam blocos, transações nem cadeias; o console continua falando com todos.
 */
export function PartitionPanel({ snapshot, actions, busy }: Props) {
  const running = snapshot.nodes.filter((node) => node.state === 'running');
  const [sides, setSides] = useState<Record<string, Side>>(() =>
    Object.fromEntries(running.map((node) => [node.id, (partitionOf(node) ?? '') as Side])),
  );

  const groups = Object.fromEntries(Object.entries(sides).filter(([, side]) => side !== '')) as Record<string, string>;

  return (
    <div>
      <table className="lab-table">
        <thead>
          <tr><th>Nó</th><th>Agora</th><th>Lado</th></tr>
        </thead>
        <tbody>
          {running.map((node) => (
            <tr key={node.id}>
              <td>{node.id}</td>
              <td>{partitionOf(node) ? `lado ${partitionOf(node)}` : <span className="muted">sem partição</span>}</td>
              <td>
                <div className="segmented" role="radiogroup" aria-label={`Lado do ${node.id}`}>
                  {(['', 'A', 'B'] as const).map((side) => (
                    <label key={side || 'none'}>
                      <input
                        type="radio"
                        name={`side-${node.id}`}
                        checked={(sides[node.id] ?? '') === side}
                        onChange={() => setSides((current) => ({ ...current, [node.id]: side }))}
                      />
                      {side ? side : 'nenhum'}
                    </label>
                  ))}
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="buttons">
        <button type="button" className="primary" disabled={busy} onClick={() => actions.partition(groups)}>
          Aplicar partição
        </button>
        <button type="button" disabled={busy} onClick={() => {
          setSides(Object.fromEntries(running.map((node) => [node.id, '' as Side])));
          return actions.partition({});
        }}>
          Curar (todos juntos de novo)
        </button>
      </div>
    </div>
  );
}
