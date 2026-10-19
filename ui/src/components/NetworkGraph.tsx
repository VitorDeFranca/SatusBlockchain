import type { KeyboardEvent } from 'react';
import type { NetworkSnapshot } from '../api/types';
import { circleLayout, edgesOf, partitionOf, statusInfo } from '../domain/network';

const WIDTH = 640;
const HEIGHT = 440;
const RADIUS = 36;

interface Props {
  snapshot: NetworkSnapshot;
  selectedId: string | null;
  grown: string[];
  onSelect: (id: string) => void;
}

/**
 * A rede como grafo: um círculo por nó, uma linha por par de peers. Linha cheia = os dois se
 * listam; tracejada com seta = só um lista o outro (cada nó tem a SUA visão da rede). O
 * estado aparece em cor, símbolo e texto — nunca só em cor.
 */
export function NetworkGraph({ snapshot, selectedId, grown, onSelect }: Props) {
  const nodes = snapshot.nodes;

  if (nodes.length === 0) {
    return (
      <p className="empty">
        Nenhum nó encontrado. Suba os nós (<code>docker compose up -d</code>) ou informe <code>GATEWAY_NODES</code> ao console.
      </p>
    );
  }

  const positions = circleLayout(nodes.length, WIDTH, HEIGHT);
  const at = new Map(nodes.map((node, i) => [node.id, positions[i]]));
  const edges = edgesOf(nodes);

  const keyboard = (id: string) => (event: KeyboardEvent) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      onSelect(id);
    }
  };

  return (
    <div className="graph">
      <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} role="group" aria-label="Grafo da rede: nós e ligações entre peers">
        <defs>
          <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
            <path d="M 0 0 L 10 5 L 0 10 z" className="edge__arrow" />
          </marker>
        </defs>

        {edges.map((edge) => {
          const from = at.get(edge.from)!;
          const to = at.get(edge.to)!;
          const [x1, y1, x2, y2] = trim(from, to, RADIUS + 4);
          return (
            <line
              key={`${edge.a}-${edge.b}`}
              data-testid={`edge-${edge.a}-${edge.b}`}
              className={['edge', edge.mutual ? '' : 'edge--one-way', edge.cut ? 'edge--cut' : ''].filter(Boolean).join(' ')}
              x1={x1}
              y1={y1}
              x2={x2}
              y2={y2}
              markerEnd={edge.mutual ? undefined : 'url(#arrow)'}
            >
              <title>
                {edge.cut
                  ? `partição: ${edge.a} e ${edge.b} estão em lados diferentes (não trocam nada)`
                  : edge.mutual ? `${edge.a} ⇄ ${edge.b}` : `só ${edge.from} lista ${edge.to}`}
              </title>
            </line>
          );
        })}

        {nodes.map((node) => {
          const position = at.get(node.id)!;
          const info = statusInfo(node, snapshot.summary.maxLength);
          const classes = [
            'node',
            `node--${info.tone}`,
            node.role === 'dynamic' ? 'node--dynamic' : '',
            node.id === selectedId ? 'node--selected' : '',
            grown.includes(node.id) ? 'node--pulse' : '',
          ].filter(Boolean);
          const blocks = node.chain ? `${node.chain.length} ${node.chain.length === 1 ? 'bloco' : 'blocos'}` : info.label;
          const partition = partitionOf(node);
          const delay = node.lab?.delayMs ?? 0;
          // Linhas abaixo do círculo: pendentes e lentidão, uma embaixo da outra.
          const below = [
            node.mempool ? `${node.mempool} pendente${node.mempool === 1 ? '' : 's'}` : null,
            delay > 0 ? `⏱ lento ${delay >= 1000 ? `${delay / 1000} s` : `${delay} ms`}` : null,
          ].filter((line): line is string => line !== null);

          return (
            <g
              key={node.id}
              className={classes.join(' ')}
              transform={`translate(${position.x} ${position.y})`}
              role="button"
              tabIndex={0}
              aria-pressed={node.id === selectedId}
              aria-label={[
                `${node.id}: ${info.label}`,
                node.chain ? blocks : null,
                partition ? `lado ${partition} da partição` : null,
                delay > 0 ? `lento (${delay} ms)` : null,
              ].filter(Boolean).join(', ')}
              onClick={() => onSelect(node.id)}
              onKeyDown={keyboard(node.id)}
            >
              <circle className="node__halo" r={RADIUS + 9} />
              <circle className="node__body" r={RADIUS} />
              <text className="node__id" y={-5}>
                {node.id}
              </text>
              <text className="node__meta" y={13}>
                {blocks}
              </text>
              <g className="node__badge" transform={`translate(${RADIUS * 0.74} ${-RADIUS * 0.74})`}>
                <circle r={11} />
                <text y={4}>{info.symbol}</text>
              </g>
              {partition ? (
                <g className="node__partition" transform={`translate(${-RADIUS * 0.74} ${-RADIUS * 0.74})`}>
                  <rect x={-11} y={-11} width={22} height={22} rx={4} />
                  <text y={4}>{partition.slice(0, 2)}</text>
                </g>
              ) : null}
              {below.map((line, i) => (
                <text key={line} className="node__mempool" y={RADIUS + 20 + i * 14}>
                  {line}
                </text>
              ))}
            </g>
          );
        })}
      </svg>

      <ul className="legend" aria-label="Legenda">
        <li><span className="dot dot--ok">✓</span>em dia</li>
        <li><span className="dot dot--warn">↓</span>atrasado</li>
        <li><span className="dot dot--fork">⑂</span>fork</li>
        <li><span className="dot dot--down">✕</span>fora do ar</li>
        <li><span className="dot dot--stopped">■</span>parado</li>
        <li><span className="dot dot--bad">≠</span>outra rede / inválido</li>
        <li><span className="line" />peers mútuos</li>
        <li><span className="line line--one-way" />só um lista o outro</li>
        <li><span className="ring ring--dynamic" />borda tracejada: nó dinâmico</li>
        <li><span className="line line--cut" />partição (lados diferentes)</li>
        <li><span className="square">B</span>lado da partição</li>
        <li>⏱ nó lento para os peers</li>
      </ul>
    </div>
  );
}

/** Encurta a linha para ela começar e terminar na borda dos círculos, não no centro. */
function trim(from: { x: number; y: number }, to: { x: number; y: number }, by: number): [number, number, number, number] {
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  const length = Math.hypot(dx, dy) || 1;
  const ux = (dx / length) * by;
  const uy = (dy / length) * by;
  return [from.x + ux, from.y + uy, to.x - ux, to.y - uy];
}
