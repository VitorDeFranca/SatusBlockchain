import type { Dispatch, ReactNode, SetStateAction } from 'react';
import type { NetworkSnapshot } from '../../api/types';
import type { Activity } from '../../domain/messages';
import { statusInfo } from '../../domain/network';
import type { NodeActions } from '../../hooks/useNodeActions';

/**
 * Onde o roteiro está. Mora FORA deste componente (no App): trocar de aba desmonta o
 * laboratório — inclusive o próprio passo 4, que leva ao explorador — e, ao voltar, o
 * roteiro tem que continuar de onde parou.
 */
export interface ForkProgress {
  /** Nó escolhido para isolar; null = o padrão (o último nó respondendo). */
  isolated: string | null;
  /** Índice do passo atual (0 = nenhum executado). */
  next: number;
  /** Um passo está executando — vale também se o operador saiu da aba no meio dele. */
  working: boolean;
}

export const INITIAL_FORK_PROGRESS: ForkProgress = { isolated: null, next: 0, working: false };

interface Props {
  snapshot: NetworkSnapshot;
  actions: NodeActions;
  progress: ForkProgress;
  onProgress: Dispatch<SetStateAction<ForkProgress>>;
  onCompare: (a: string, b: string) => void;
}

interface Step {
  title: string;
  text: string;
  run?: () => Promise<Activity | void>;
  extra?: ReactNode;
}

/**
 * O roteiro da etapa 12 em passos guiados: isolar um nó, minerar dos dois lados (fork), curar
 * a partição e sincronizar — a convergência pela longest chain rule, com o bloco do lado
 * perdedor virando órfão. Um passo de cada vez, para dar tempo de explicar cada um.
 */
export function ForkScenario({ snapshot, actions, progress, onProgress, onCompare }: Props) {
  const running = snapshot.nodes.filter((node) => node.state === 'running');
  const { next, working } = progress;
  // O escolhido saiu do ar (ou nada foi escolhido)? Vale o padrão: o último respondendo.
  const isolated = running.find((node) => node.id === progress.isolated)?.id ?? running[running.length - 1]?.id ?? '';

  const main = running.find((node) => node.id !== isolated)?.id;

  if (running.length < 2 || !main) {
    return <p className="muted">O roteiro precisa de pelo menos dois nós respondendo.</p>;
  }

  const live = (id: string) => {
    const node = snapshot.nodes.find((n) => n.id === id);
    return node ? statusInfo(node, snapshot.summary.maxLength) : null;
  };

  // Lado A: todos os outros; lado B: o isolado.
  const sides = Object.fromEntries(running.map((node) => [node.id, node.id === isolated ? 'B' : 'A']));

  const steps: Step[] = [
    {
      title: `Isolar o ${isolated}`,
      text: `O ${isolated} vai para o lado B; os demais, para o lado A. Lados diferentes recusam o tráfego entre si (503) — mas o console continua operando os dois.`,
      run: () => actions.partition(sides),
    },
    {
      title: `Minerar no lado isolado (${isolated})`,
      text: 'Uma transação e um bloco que só o lado B conhece: o push dele para o lado A é recusado.',
      run: async () => {
        await actions.transaction(isolated, { from: 'carol', to: 'dave', amount: 7 });
        return actions.mine(isolated);
      },
    },
    {
      title: `Minerar dois blocos no lado principal (${main})`,
      text: 'O lado A cresce mais: uma transação e duas minerações. As duas metades agora têm histórias diferentes — um fork.',
      run: async () => {
        await actions.transaction(main, { from: 'alice', to: 'bob', amount: 10 });
        await actions.mine(main);
        return actions.mine(main);
      },
    },
    {
      title: 'Observar o fork',
      text: `No grafo, o ${isolated} aparece em fork (⑂): o topo dele não está na cadeia líder. A comparação no explorador mostra o bloco onde as cadeias se separam.`,
      extra: (
        <button type="button" onClick={() => onCompare(main, isolated)}>
          Comparar {main} × {isolated} no explorador
        </button>
      ),
    },
    {
      title: 'Curar a partição',
      text: 'Os nós voltam a se enxergar — mas nada converge sozinho: o push só leva blocos NOVOS, e o do lado B não encaixa na cadeia do lado A (409).',
      run: () => actions.partition({}),
    },
    {
      title: 'Sincronizar todos',
      text: 'Cada nó puxa a cadeia válida mais longa dos peers. O bloco do lado B vira órfão, e a transação dele volta para a mempool: consistência eventual.',
      run: () => actions.syncAll(),
    },
  ];

  async function execute(index: number) {
    // Fixa o isolado: um nó que entre no meio do roteiro não pode virar o "último" e trocar de lado.
    onProgress((current) => ({ ...current, isolated, working: true }));
    try {
      await steps[index].run?.();
      onProgress((current) => ({ ...current, next: index + 1 }));
    } finally {
      onProgress((current) => ({ ...current, working: false }));
    }
  }

  const isolatedInfo = live(isolated);
  const mainInfo = live(main);

  return (
    <div className="scenario">
      <div className="scenario__setup">
        <label>
          Nó a isolar
          <select value={isolated} onChange={(e) => onProgress({ ...INITIAL_FORK_PROGRESS, isolated: e.target.value })}
            disabled={working || next > 0}>
            {running.map((node) => <option key={node.id} value={node.id}>{node.id}</option>)}
          </select>
        </label>
        <span className="muted">
          agora: {isolated} {isolatedInfo ? `${isolatedInfo.symbol} ${isolatedInfo.label}` : '—'} · {main}{' '}
          {mainInfo ? `${mainInfo.symbol} ${mainInfo.label}` : '—'}
        </span>
        <button type="button" onClick={() => onProgress((current) => ({ ...current, next: 0 }))}
          disabled={working || next === 0}>Recomeçar</button>
      </div>

      <ol className="steps">
        {steps.map((step, index) => {
          const state = index < next ? 'done' : index === next ? 'current' : 'pending';
          return (
            <li key={step.title} className={`step step--${state}`}>
              <div className="step__head">
                <span className="step__marker" aria-hidden="true">{state === 'done' ? '✓' : index + 1}</span>
                <strong>{step.title}</strong>
              </div>
              <p>{step.text}</p>
              <div className="buttons">
                {state === 'current' ? (
                  <button type="button" className="primary" onClick={() => execute(index)} disabled={working}>
                    {working ? 'Executando…' : step.run ? 'Executar' : 'Feito, próximo'}
                  </button>
                ) : null}
                {state !== 'pending' ? step.extra : null}
              </div>
            </li>
          );
        })}
      </ol>
    </div>
  );
}
