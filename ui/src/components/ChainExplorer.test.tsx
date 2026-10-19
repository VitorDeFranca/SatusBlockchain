import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Block } from '../api/types';
import { nodeView, snapshotOf } from '../test/fixtures';
import { ChainExplorer } from './ChainExplorer';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function chainOf(...hashes: string[]): Block[] {
  return hashes.map((hash, index) => ({
    index,
    timestamp: '2026-01-01T00:00:00Z',
    transactions: index === 1 ? [{ from: 'carol', to: 'dave', amount: 7 }] : [],
    previousHash: index === 0 ? '0' : hashes[index - 1],
    nonce: index,
    hash,
  }));
}

/** O gateway de mentira: GET /api/nodes/{id}/chain devolve a cadeia combinada para o nó. */
function stubChains(chains: Record<string, Block[]>) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const id = /\/api\/nodes\/([^/]+)\/chain/.exec(url)?.[1];
    const body = id && chains[id] ? chains[id] : { error: 'não encontrado' };
    return new Response(JSON.stringify(body), { status: id && chains[id] ? 200 : 404 });
  }));
}

function renderExplorer(initialCompareId: string | null) {
  const snapshot = snapshotOf([nodeView('node1'), nodeView('node3')]);
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ChainExplorer snapshot={snapshot} initialNodeId="node1" initialCompareId={initialCompareId} onTamper={() => {}} />
    </QueryClientProvider>,
  );
}

describe('ChainExplorer', () => {
  it('lista a cadeia do topo para o genesis, com o elo de cada bloco', async () => {
    stubChains({ node1: chainOf('00g', '00a1', '00a2') });
    renderExplorer(null);

    const cards = await screen.findAllByRole('button', { name: /^#\d/ });
    expect(cards.map((card) => card.textContent?.slice(0, 2))).toEqual(['#2', '#1', '#0']);
    expect(screen.getByText('↓ elo com o #1')).toBeTruthy();
  });

  it('comparando dois nós, aponta o ponto de fork', async () => {
    stubChains({ node1: chainOf('00g', '00a1', '00a2'), node3: chainOf('00g', '00b1') });
    renderExplorer('node3');

    expect(await screen.findByText(/^Fork no bloco #1/)).toBeTruthy();
    expect(screen.getByText('⑂ ponto de fork')).toBeTruthy();
    expect(screen.getByText('só no node1')).toBeTruthy();
  });

  it('clicar num bloco abre o detalhe com as transações', async () => {
    stubChains({ node1: chainOf('00g', '00a1') });
    renderExplorer(null);

    fireEvent.click((await screen.findAllByRole('button', { name: /^#1/ }))[0]);

    expect(await screen.findByText('Bloco #1')).toBeTruthy();
    expect(screen.getByRole('cell', { name: 'carol' })).toBeTruthy();
  });
});
