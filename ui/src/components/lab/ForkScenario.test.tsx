import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { NetworkSnapshot } from '../../api/types';
import type { NodeActions } from '../../hooks/useNodeActions';
import { nodeView, snapshotOf } from '../../test/fixtures';
import { ForkScenario, INITIAL_FORK_PROGRESS } from './ForkScenario';

afterEach(cleanup);

/** Ações de mentira: cada uma registra a chamada e "dá certo". */
function fakeActions() {
  const done = async () => ({ ok: true, node: 'x', action: 'x', message: 'ok' });
  return {
    busy: {},
    partition: vi.fn(done),
    transaction: vi.fn(done),
    mine: vi.fn(done),
    syncAll: vi.fn(done),
  } as unknown as NodeActions & Record<'partition' | 'transaction' | 'mine' | 'syncAll', ReturnType<typeof vi.fn>>;
}

/** Faz o papel do App: guarda o progresso FORA do roteiro, que pode ser desmontado (troca de aba). */
function Harness({ snapshot, actions, shown = true }: { snapshot: NetworkSnapshot; actions: NodeActions; shown?: boolean }) {
  const [progress, setProgress] = useState(INITIAL_FORK_PROGRESS);
  return shown ? (
    <ForkScenario snapshot={snapshot} actions={actions} progress={progress} onProgress={setProgress} onCompare={() => {}} />
  ) : null;
}

/** Estado visual do passo cujo título é `title`: done, current ou pending. */
function stepState(title: string) {
  return screen.getByText(title).closest('li')?.className.match(/step--(\w+)/)?.[1];
}

describe('ForkScenario', () => {
  const snapshot = snapshotOf([nodeView('node1'), nodeView('node2'), nodeView('node3')]);

  it('passo 1 isola o último nó no lado B e os demais no lado A; depois libera o passo 2', async () => {
    const actions = fakeActions();
    render(<Harness snapshot={snapshot} actions={actions} />);

    fireEvent.click(screen.getByRole('button', { name: 'Executar' }));

    expect(actions.partition).toHaveBeenCalledWith({ node1: 'A', node2: 'A', node3: 'B' });
    expect(await screen.findByText('Minerar no lado isolado (node3)')).toBeTruthy();
    // O passo 1 aparece concluído e o botão "Executar" agora é do passo 2.
    await vi.waitFor(() => expect(stepState('Isolar o node3')).toBe('done'));
    expect(screen.getAllByRole('button', { name: 'Executar' })).toHaveLength(1);
  });

  it('passo 2 cria uma transação e minera no nó isolado', async () => {
    const actions = fakeActions();
    render(<Harness snapshot={snapshot} actions={actions} />);

    fireEvent.click(screen.getByRole('button', { name: 'Executar' }));
    await vi.waitFor(() => expect(stepState('Isolar o node3')).toBe('done'));
    fireEvent.click(await screen.findByRole('button', { name: 'Executar' }));
    await vi.waitFor(() => expect(actions.mine).toHaveBeenCalledWith('node3'));

    expect(actions.transaction).toHaveBeenCalledWith('node3', { from: 'carol', to: 'dave', amount: 7 });
  });

  it('sair da aba e voltar mantém o passo em que o roteiro estava', async () => {
    const actions = fakeActions();
    const { rerender } = render(<Harness snapshot={snapshot} actions={actions} />);

    fireEvent.click(screen.getByRole('button', { name: 'Executar' }));
    await vi.waitFor(() => expect(stepState('Isolar o node3')).toBe('done'));

    rerender(<Harness snapshot={snapshot} actions={actions} shown={false} />);
    expect(screen.queryByText('Isolar o node3')).toBeNull();
    rerender(<Harness snapshot={snapshot} actions={actions} />);

    expect(stepState('Isolar o node3')).toBe('done');
    expect(stepState('Minerar no lado isolado (node3)')).toBe('current');
  });

  it('um nó que entra no meio do roteiro não vira o isolado', async () => {
    const actions = fakeActions();
    const { rerender } = render(<Harness snapshot={snapshot} actions={actions} />);

    fireEvent.click(screen.getByRole('button', { name: 'Executar' }));
    await vi.waitFor(() => expect(stepState('Isolar o node3')).toBe('done'));

    const grown = snapshotOf([nodeView('node1'), nodeView('node2'), nodeView('node3'), nodeView('node4')]);
    rerender(<Harness snapshot={grown} actions={actions} />);

    expect(screen.getByText('Minerar no lado isolado (node3)')).toBeTruthy();
  });

  it('com um nó só, explica que precisa de dois', () => {
    render(<Harness snapshot={snapshotOf([nodeView('node1')])} actions={fakeActions()} />);
    expect(screen.getByText(/pelo menos dois nós/)).toBeTruthy();
  });
});
