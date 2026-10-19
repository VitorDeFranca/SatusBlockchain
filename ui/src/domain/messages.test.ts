import { describe, expect, it } from 'vitest';
import type { ApiResult } from '../api/client';
import type { Block, SyncReport } from '../api/types';
import {
  describeCreate,
  describeDelay,
  describeDelivery,
  describeMine,
  describePartition,
  describeStop,
  describeSync,
  describeSyncAll,
  describeTransaction,
} from './messages';

const ok = <T>(data: T, status = 200): ApiResult<T> => ({ ok: true, status, data, error: null });
const fail = <T>(status: number, error: string): ApiResult<T> => ({ ok: false, status, data: null, error });

describe('mensagens do registro', () => {
  it('bloco minerado', () => {
    const block: Block = { index: 3, timestamp: '', transactions: [{ from: 'a', to: 'b', amount: 1 }], previousHash: '00aa', nonce: 42, hash: '00bb' };
    const activity = describeMine('node1', ok(block));

    expect(activity.ok).toBe(true);
    expect(activity.message).toBe('Bloco #3 minerado no node1 com 1 transação e enviado aos peers.');
    expect(activity.details).toContain('nonce 42');
  });

  it('a recusa do nó chega com a mensagem dele', () => {
    const activity = describeMine('node1', fail(409, 'A cadeia mudou durante a mineração. Tente minerar novamente.'));

    expect(activity.ok).toBe(false);
    expect(activity.message).toBe('Recusado (409): A cadeia mudou durante a mineração. Tente minerar novamente.');
  });

  it('console fora do ar é dito como tal', () => {
    const activity = describeTransaction('node1', { from: 'a', to: 'b', amount: 1 }, fail(0, 'x'));
    expect(activity.message).toContain('console não respondeu');
  });

  it('sync que adotou cadeia troca URLs por nomes e explica cada peer', () => {
    const report: SyncReport = {
      node: 'node3',
      previousLength: 1,
      length: 4,
      adopted: { from: 'http://node1:8080', length: 4 },
      orphansBackToMempool: 2,
      peers: [
        { peer: 'http://node1:8080', outcome: 'Adopted', detail: 'cadeia mais longa (4 blocos) adotada' },
        { peer: 'http://node9:8080', outcome: 'Unreachable', detail: 'inacessível' },
      ],
    };
    const names: Record<string, string> = { 'http://node1:8080': 'node1' };

    const activity = describeSync('node3', ok(report), (url) => names[url] ?? url);

    expect(activity.message).toBe('node3 adotou a cadeia de node1 (1 → 4 blocos); 2 transações órfãs voltaram para a mempool.');
    expect(activity.details).toEqual([
      'node1: adotada — cadeia mais longa (4 blocos) adotada',
      'http://node9:8080: sem resposta — inacessível',
    ]);
  });

  it('sync sem adoção', () => {
    const report: SyncReport = { node: 'node1', previousLength: 3, length: 3, adopted: null, orphansBackToMempool: 0, peers: [] };
    expect(describeSync('node1', ok(report), (url) => url).message).toContain('manteve a própria cadeia (3 blocos)');
  });

  it('nó criado e queda explicam o que acontece na rede', () => {
    const created = describeCreate(ok({ id: 'node4', url: 'http://node4:8080', hostPort: 8083, seed: 'node1', difficulty: 4 }, 201));
    expect(created.node).toBe('node4');
    expect(created.message).toContain('semente node1 (dificuldade 4)');
    expect(created.message).toContain('localhost:8083');

    expect(describeStop('node4', 'crash', ok({})).message).toContain('3 chamadas sem resposta');
  });
});

describe('mensagens do laboratório', () => {
  const results = (...nodes: string[]) => nodes.map((node) => ({ node, status: 200, body: {} }));

  it('partição lista os lados; curar avisa que o fork continua até o sync', () => {
    const applied = describePartition({ node1: 'A', node2: 'A', node3: 'B' }, ok(results('node1', 'node2', 'node3')));
    expect(applied.message).toContain('lado A: node1, node2 · lado B: node3');

    const healed = describePartition({}, ok(results('node1')));
    expect(healed.message).toContain('Os forks continuam até alguém sincronizar');
  });

  it('sincronizar todos resume quem adotou e as órfãs, e detalha cada nó', () => {
    const report = (adopted: boolean, orphans: number): SyncReport => ({
      node: 'x',
      previousLength: 2,
      length: 3,
      adopted: adopted ? { from: 'http://node1:8080', length: 3 } : null,
      orphansBackToMempool: orphans,
      peers: [],
    });

    const activity = describeSyncAll(
      ok([
        { node: 'node1', status: 200, body: report(false, 0) },
        { node: 'node3', status: 200, body: report(true, 1) },
      ]),
      (url) => (url === 'http://node1:8080' ? 'node1' : url),
    );

    expect(activity.message).toBe('1 nó adotou a cadeia mais longa; 1 transação órfã voltou para a mempool.');
    expect(activity.details).toEqual(['node1: manteve a própria (3 blocos)', 'node3: adotou a cadeia de node1 (2 → 3 blocos)']);
  });

  it('lentidão acima do timeout explica o efeito', () => {
    expect(describeDelay('node2', 3000, ok({})).message).toContain('é como se tivesse caído');
    expect(describeDelay('node2', 0, ok({})).message).toContain('sem atraso');
  });

  it('bloco adulterado recusado: a recusa é o resultado esperado e vem explicada', () => {
    const block: Block = { index: 1, timestamp: '', transactions: [], previousHash: 'g', nonce: 0, hash: 'h' };
    const activity = describeDelivery('node2', block, fail(409, 'Bloco não estende a cadeia local (duplicado, órfão ou inválido).'));

    expect(activity.ok).toBe(false);
    expect(activity.message).toContain('recusou o bloco #1 (409)');
    expect(activity.message).toContain('defesa contra um peer adulterador');
  });
});
