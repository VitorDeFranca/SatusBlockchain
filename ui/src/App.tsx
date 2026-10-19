import { useQuery } from '@tanstack/react-query';
import { useCallback, useRef, useState } from 'react';
import { api } from './api/client';
import { ActivityLog, type LoggedActivity } from './components/ActivityLog';
import { ChainExplorer } from './components/ChainExplorer';
import { Header } from './components/Header';
import { Lab } from './components/lab/Lab';
import { NetworkGraph } from './components/NetworkGraph';
import { NodePanel } from './components/NodePanel';
import type { Activity } from './domain/messages';
import { useNetwork } from './hooks/useNetwork';
import { CREATE_KEY, useNodeActions } from './hooks/useNodeActions';

const MAX_LOG = 50;

const TABS = [
  { id: 'rede', label: 'Rede' },
  { id: 'explorador', label: 'Explorador da cadeia' },
  { id: 'laboratorio', label: 'Laboratório de falhas' },
] as const;

type Tab = (typeof TABS)[number]['id'];

/** A aba vem do # da URL — recarregar a página (ou mandar o link) mantém onde se estava. */
function tabFromHash(): Tab {
  const hash = window.location.hash.slice(1);
  return TABS.some((tab) => tab.id === hash) ? (hash as Tab) : 'rede';
}

export function App() {
  const network = useNetwork();
  const capabilities = useQuery({ queryKey: ['capabilities'], queryFn: api.capabilities, staleTime: Infinity });
  const [tab, setTab] = useState<Tab>(tabFromHash);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [comparePreset, setComparePreset] = useState<{ a: string; b: string } | null>(null);
  const [tamperPreset, setTamperPreset] = useState<{ node: string; index: number } | null>(null);
  const [log, setLog] = useState<LoggedActivity[]>([]);
  const sequence = useRef(0);

  const append = useCallback((activity: Activity) => {
    const entry = { ...activity, id: ++sequence.current, at: new Date() };
    setLog((current) => [entry, ...current].slice(0, MAX_LOG));
  }, []);

  const snapshot = network.data;
  const actions = useNodeActions(snapshot, append);

  // O selecionado some (nó removido)? Cai no primeiro da lista.
  const selected = snapshot?.nodes.find((node) => node.id === selectedId) ?? snapshot?.nodes[0] ?? null;
  const canManage = capabilities.data?.canManageNodes ?? false;

  function go(next: Tab) {
    setTab(next);
    window.history.replaceState(null, '', `#${next}`);
  }

  return (
    <div className="app">
      <Header
        snapshot={snapshot}
        capabilities={capabilities.data}
        creating={actions.busy[CREATE_KEY]}
        onAddNode={() => actions.create()}
      />

      <nav className="tabs" role="tablist" aria-label="Seções do console">
        {TABS.map((item) => (
          <button key={item.id} type="button" role="tab" aria-selected={tab === item.id}
            className={tab === item.id ? 'tab tab--active' : 'tab'} onClick={() => go(item.id)}>
            {item.label}
          </button>
        ))}
      </nav>

      {network.isError ? (
        <div className="banner" role="alert">
          O console não está conseguindo falar com o gateway ({network.error.message}). Tentando de novo a cada 2 s…
        </div>
      ) : null}

      {!snapshot ? (
        <p className="empty card">Carregando a rede…</p>
      ) : tab === 'rede' ? (
        <main className="layout">
          <section className="card graph-card" aria-label="Rede">
            <NetworkGraph snapshot={snapshot} selectedId={selected?.id ?? null} grown={network.grown} onSelect={setSelectedId} />
          </section>

          <aside className="card panel-card" aria-label="Nó selecionado">
            {selected ? (
              <NodePanel
                key={selected.id}
                node={selected}
                snapshot={snapshot}
                canManage={canManage}
                actions={actions}
                onSelect={setSelectedId}
              />
            ) : (
              <p className="empty">Selecione um nó no grafo.</p>
            )}
          </aside>
        </main>
      ) : tab === 'explorador' ? (
        <main className="card">
          <ChainExplorer
            key={comparePreset ? `${comparePreset.a}-${comparePreset.b}` : 'livre'}
            snapshot={snapshot}
            initialNodeId={comparePreset?.a ?? selected?.id ?? null}
            initialCompareId={comparePreset?.b ?? null}
            onTamper={(node, index) => {
              setTamperPreset({ node, index });
              go('laboratorio');
            }}
          />
        </main>
      ) : (
        <main>
          <Lab
            snapshot={snapshot}
            actions={actions}
            tamperPreset={tamperPreset}
            onCompare={(a, b) => {
              setComparePreset({ a, b });
              go('explorador');
            }}
          />
        </main>
      )}

      <div className="card">
        <ActivityLog entries={log} />
      </div>
    </div>
  );
}
