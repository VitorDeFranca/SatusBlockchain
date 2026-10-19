import type { Capabilities, NetworkSnapshot } from '../api/types';
import { headlineOf, offlineCount } from '../domain/network';

interface Props {
  snapshot: NetworkSnapshot | undefined;
  capabilities: Capabilities | undefined;
  creating: string | undefined;
  onAddNode: () => void;
}

/** A rede de relance (convergida? quantos fora?), links de observabilidade e "+ Adicionar nó". */
export function Header({ snapshot, capabilities, creating, onAddNode }: Props) {
  const headline = snapshot ? headlineOf(snapshot) : null;
  const offline = snapshot ? offlineCount(snapshot) : 0;
  const canManage = capabilities?.canManageNodes ?? false;

  return (
    <header className="header">
      <div className="header__title">
        <h1>SatusBlockchain</h1>
        <span className="muted">Console da rede</span>
      </div>

      <div className="header__status" aria-live="polite">
        {headline ? <span className={`pill pill--${headline.tone}`}>{headline.text}</span> : <span className="pill">Carregando…</span>}
        {offline > 0 ? <span className="pill pill--down">{offline} fora do ar ou parado{offline === 1 ? '' : 's'}</span> : null}
        {snapshot ? <span className="muted">{snapshot.mode === 'docker' ? 'Docker' : 'sem Docker'}</span> : null}
      </div>

      <nav className="header__links" aria-label="Observabilidade">
        {capabilities ? (
          <>
            <a href={capabilities.links.grafana} target="_blank" rel="noreferrer">Grafana</a>
            <a href={capabilities.links.jaeger} target="_blank" rel="noreferrer">Jaeger</a>
            <a href={capabilities.links.prometheus} target="_blank" rel="noreferrer">Prometheus</a>
          </>
        ) : null}
        <button
          type="button"
          className="primary"
          onClick={onAddNode}
          disabled={!canManage || !!creating}
          title={canManage ? 'Cria um container a partir do molde node-extra' : 'Criar nós exige o console com acesso ao Docker'}
        >
          {creating ?? '+ Adicionar nó'}
        </button>
      </nav>
    </header>
  );
}
