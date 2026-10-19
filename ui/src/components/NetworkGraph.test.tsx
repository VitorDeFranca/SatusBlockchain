import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { nodeView, peer, snapshotOf } from '../test/fixtures';
import { NetworkGraph } from './NetworkGraph';

afterEach(cleanup);

describe('NetworkGraph', () => {
  const snapshot = snapshotOf([
    nodeView('node1', { peers: [peer('node2'), peer('node4')] }),
    nodeView('node2', { peers: [peer('node1')] }),
    nodeView('node4', { role: 'dynamic', state: 'unreachable', status: 'unknown', chain: null }),
  ]);

  it('um botão por nó, com o estado em texto (não só em cor)', () => {
    render(<NetworkGraph snapshot={snapshot} selectedId="node1" grown={[]} onSelect={() => {}} />);

    expect(screen.getByRole('button', { name: 'node1: Em dia, 3 blocos' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'node4: Fora do ar' })).toBeTruthy();
  });

  it('aresta mútua é cheia; a de mão única é tracejada', () => {
    render(<NetworkGraph snapshot={snapshot} selectedId={null} grown={[]} onSelect={() => {}} />);

    expect(screen.getByTestId('edge-node1-node2').getAttribute('class')).toBe('edge');
    expect(screen.getByTestId('edge-node1-node4').getAttribute('class')).toBe('edge edge--one-way');
  });

  it('clique e teclado selecionam o nó', () => {
    const onSelect = vi.fn();
    render(<NetworkGraph snapshot={snapshot} selectedId={null} grown={['node2']} onSelect={onSelect} />);

    const node2 = screen.getByRole('button', { name: /^node2:/ });
    fireEvent.click(node2);
    fireEvent.keyDown(node2, { key: 'Enter' });

    expect(onSelect).toHaveBeenCalledTimes(2);
    expect(onSelect).toHaveBeenCalledWith('node2');
    expect(node2.getAttribute('class')).toContain('node--pulse');
  });

  it('sem nós, explica como subir a rede', () => {
    render(<NetworkGraph snapshot={snapshotOf([])} selectedId={null} grown={[]} onSelect={() => {}} />);
    expect(screen.getByText(/Nenhum nó encontrado/)).toBeTruthy();
  });
});
