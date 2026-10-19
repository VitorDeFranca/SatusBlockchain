import type { NetworkSnapshot } from '../../api/types';
import type { NodeActions } from '../../hooks/useNodeActions';

const OPTIONS = [0, 500, 1000, 3000, 5000];

interface Props {
  snapshot: NetworkSnapshot;
  actions: NodeActions;
}

/**
 * Lentidão: o nó demora para responder aos PEERS (o console não sente). Abaixo de 2 s, só
 * atrasa a propagação; acima, estoura o timeout do PeerClient — para os peers, o nó lento é
 * indistinguível de um nó caído, e um nó dinâmico sai da lista após 3 chamadas sem resposta.
 */
export function DelayPanel({ snapshot, actions }: Props) {
  const running = snapshot.nodes.filter((node) => node.state === 'running');

  return (
    <table className="lab-table">
      <thead>
        <tr><th>Nó</th><th>Atraso para peers</th></tr>
      </thead>
      <tbody>
        {running.map((node) => {
          const current = node.lab?.delayMs ?? 0;
          const options = OPTIONS.includes(current) ? OPTIONS : [...OPTIONS, current].sort((a, b) => a - b);
          return (
            <tr key={node.id}>
              <td>{node.id}</td>
              <td>
                <select
                  aria-label={`Atraso do ${node.id}`}
                  value={current}
                  disabled={!!actions.busy[node.id]}
                  onChange={(e) => actions.delay(node.id, Number(e.target.value))}
                >
                  {options.map((ms) => (
                    <option key={ms} value={ms}>
                      {ms === 0 ? 'sem atraso' : `${ms} ms${ms >= 2000 ? ' (acima do timeout)' : ''}`}
                    </option>
                  ))}
                </select>
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
