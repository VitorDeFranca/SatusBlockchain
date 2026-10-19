import { describe, expect, it } from 'vitest';
import { nodeView, peer, snapshotOf } from '../test/fixtures';
import { circleLayout, edgesOf, grownNodes, headlineOf, offlineCount, shortHash, statusInfo } from './network';

describe('edgesOf', () => {
  it('liga quem se lista nos dois sentidos com uma aresta mútua só', () => {
    const edges = edgesOf([nodeView('node1', { peers: [peer('node2')] }), nodeView('node2', { peers: [peer('node1')] })]);

    expect(edges).toEqual([{ a: 'node1', b: 'node2', mutual: true, from: 'node1', to: 'node2', cut: false }]);
  });

  it('marca como mão única quando só um lista o outro (visão local de cada nó)', () => {
    const edges = edgesOf([nodeView('node1', { peers: [peer('node4')] }), nodeView('node4')]);

    expect(edges).toEqual([{ a: 'node1', b: 'node4', mutual: false, from: 'node1', to: 'node4', cut: false }]);
  });

  it('marca como cortada a ligação entre lados diferentes da partição', () => {
    const edges = edgesOf([
      nodeView('node1', { peers: [peer('node2'), peer('node3')] }),
      nodeView('node2', { peers: [peer('node1')], lab: { partition: null, delayMs: 0 } }),
      nodeView('node3', { peers: [peer('node1')], lab: { partition: 'B', delayMs: 0 } }),
    ]);

    expect(edges.find((e) => e.b === 'node2')?.cut).toBe(false);
    expect(edges.find((e) => e.b === 'node3')?.cut).toBe(true);
  });

  it('ignora peers desconhecidos e o próprio nó', () => {
    const edges = edgesOf([
      nodeView('node1', { peers: [{ url: 'http://fantasma:8080', nodeId: null }, peer('node1'), peer('node9')] }),
    ]);

    expect(edges).toEqual([]);
  });
});

describe('statusInfo', () => {
  it('atrasado diz quantos blocos faltam', () => {
    const info = statusInfo(nodeView('node3', { status: 'behind', chain: { length: 1, latestHash: 'g', valid: true } }), 4);

    expect(info.label).toBe('Atrasado');
    expect(info.description).toContain('3 blocos atrás');
  });

  it('o estado da plataforma vem antes do status da cadeia', () => {
    expect(statusInfo(nodeView('node2', { state: 'unreachable' }), 3).label).toBe('Fora do ar');
    expect(statusInfo(nodeView('node2', { state: 'stopped', role: 'seed' }), 3).description).toContain('Sincronizar');
  });

  it('todo estado tem símbolo além da cor', () => {
    for (const status of ['ok', 'behind', 'diverged', 'invalid', 'incompatible', 'unknown'] as const)
      expect(statusInfo(nodeView('n', { status }), 3).symbol).not.toBe('');
  });
});

describe('headlineOf', () => {
  it('rede convergida', () => {
    const snapshot = snapshotOf([nodeView('node1'), nodeView('node2')]);
    expect(headlineOf(snapshot)).toEqual({ text: 'Rede convergida · 2 nós · 3 blocos', tone: 'ok' });
  });

  it('rede divergente conta atrasados e forks', () => {
    const snapshot = snapshotOf([
      nodeView('node1'),
      nodeView('node2', { status: 'behind', chain: { length: 1, latestHash: 'g', valid: true } }),
      nodeView('node3', { status: 'diverged' }),
    ]);
    expect(headlineOf(snapshot)).toEqual({ text: 'Rede divergente: 1 atrasado, 1 em fork', tone: 'warn' });
  });

  it('ninguém respondendo', () => {
    const snapshot = snapshotOf([nodeView('node1', { state: 'unreachable', chain: null })]);
    expect(headlineOf(snapshot).text).toBe('Nenhum nó respondendo');
    expect(offlineCount(snapshot)).toBe(1);
  });
});

describe('grownNodes', () => {
  it('aponta os nós cuja cadeia cresceu desde o último poll', () => {
    const before = snapshotOf([nodeView('node1'), nodeView('node2')]);
    const after = snapshotOf([
      nodeView('node1', { chain: { length: 4, latestHash: 'x', valid: true } }),
      nodeView('node2'),
      nodeView('node4', { chain: { length: 9, latestHash: 'x', valid: true } }), // nó novo não pulsa
    ]);

    expect(grownNodes(before, after)).toEqual(['node1']);
    expect(grownNodes(undefined, after)).toEqual([]);
  });
});

describe('utilitários', () => {
  it('circleLayout distribui os nós dentro da área', () => {
    const points = circleLayout(5, 640, 440);
    expect(points).toHaveLength(5);
    for (const { x, y } of points) {
      expect(x).toBeGreaterThan(0);
      expect(x).toBeLessThan(640);
      expect(y).toBeGreaterThan(0);
      expect(y).toBeLessThan(440);
    }
  });

  it('shortHash encurta e trata ausência', () => {
    expect(shortHash('0000abcdef1234567890')).toBe('0000abcdef12…');
    expect(shortHash(null)).toBe('—');
  });
});
