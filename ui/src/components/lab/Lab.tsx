import type { NetworkSnapshot } from '../../api/types';
import { LAB_KEY, type NodeActions } from '../../hooks/useNodeActions';
import { DelayPanel } from './DelayPanel';
import { ForkScenario } from './ForkScenario';
import { PartitionPanel } from './PartitionPanel';
import { TamperPanel } from './TamperPanel';

interface Props {
  snapshot: NetworkSnapshot;
  actions: NodeActions;
  tamperPreset: { node: string; index: number } | null;
  onCompare: (a: string, b: string) => void;
}

/**
 * Laboratório de falhas e fork (etapa 12). As falhas valem só para o tráfego ENTRE nós — o
 * console continua operando todos, inclusive os dois lados de uma partição. Tudo volta ao
 * normal com "Remover todas as falhas" (ou reiniciando os nós).
 */
export function Lab({ snapshot, actions, tamperPreset, onCompare }: Props) {
  const busy = actions.busy[LAB_KEY];

  return (
    <div className="lab">
      <div className="card lab__bar">
        <div>
          <h2>Laboratório de falhas e fork</h2>
          <p className="muted">
            As falhas atingem só o tráfego entre nós: o console continua falando com todos. {busy ? <strong>{busy}</strong> : null}
          </p>
        </div>
        <div className="buttons">
          <button type="button" onClick={() => actions.syncAll()} disabled={!!busy}>Sincronizar todos</button>
          <button type="button" className="danger" onClick={() => actions.resetLab()} disabled={!!busy}>Remover todas as falhas</button>
        </div>
      </div>

      <section className="card" aria-labelledby="lab-fork">
        <h3 id="lab-fork">Roteiro: fork e convergência</h3>
        <ForkScenario snapshot={snapshot} actions={actions} onCompare={onCompare} />
      </section>

      <div className="lab__grid">
        <section className="card" aria-labelledby="lab-partition">
          <h3 id="lab-partition">Partição de rede</h3>
          <p className="muted">Lados diferentes não trocam blocos, transações nem cadeias (503). No grafo, a ligação fica vermelha.</p>
          <PartitionPanel key={snapshot.nodes.map((n) => n.id).join()} snapshot={snapshot} actions={actions} busy={!!busy} />
        </section>

        <section className="card" aria-labelledby="lab-delay">
          <h3 id="lab-delay">Nó lento</h3>
          <p className="muted">Acima de 2 s (o timeout entre nós), um nó lento é, para os peers, um nó caído.</p>
          <DelayPanel snapshot={snapshot} actions={actions} />
        </section>
      </div>

      <section className="card" aria-labelledby="lab-tamper">
        <h3 id="lab-tamper">Bloco adulterado</h3>
        <TamperPanel key={tamperPreset ? `${tamperPreset.node}-${tamperPreset.index}` : 'livre'}
          snapshot={snapshot} actions={actions} preset={tamperPreset} />
      </section>
    </div>
  );
}
