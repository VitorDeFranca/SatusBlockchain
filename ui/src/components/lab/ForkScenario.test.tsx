import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { NodeActions } from '../../hooks/useNodeActions';
import { nodeView, snapshotOf } from '../../test/fixtures';
import { ForkScenario } from './ForkScenario';

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

describe('ForkScenario', () => {
  const snapshot = snapshotOf([nodeView('node1'), nodeView('node2'), nodeView('node3')]);

  it('passo 1 isola o último nó no lado B e os demais no lado A; depois libera o passo 2', async () => {
    const actions = fakeActions();
    render(<ForkScenario snapshot={snapshot} actions={actions} onCompare={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: 'Executar' }));

    expect(actions.partition).toHaveBeenCalledWith({ node1: 'A', node2: 'A', node3: 'B' });
    expect(await screen.findByText('Minerar no lado isolado (node3)')).toBeTruthy();
    // O passo 1 aparece concluído e o botão "Executar" agora é do passo 2.
    expect(screen.getAllByRole('button', { name: 'Executar' })).toHaveLength(1);
  });

  it('passo 2 cria uma transação e minera no nó isolado', async () => {
    const actions = fakeActions();
    render(<ForkScenario snapshot={snapshot} actions={actions} onCompare={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: 'Executar' }));
    await screen.findByText('Minerar no lado isolado (node3)');
    fireEvent.click(await screen.findByRole('button', { name: 'Executar' }));
    await vi.waitFor(() => expect(actions.mine).toHaveBeenCalledWith('node3'));

    expect(actions.transaction).toHaveBeenCalledWith('node3', { from: 'carol', to: 'dave', amount: 7 });
  });

  it('com um nó só, explica que precisa de dois', () => {
    render(<ForkScenario snapshot={snapshotOf([nodeView('node1')])} actions={fakeActions()} onCompare={() => {}} />);
    expect(screen.getByText(/pelo menos dois nós/)).toBeTruthy();
  });
});
