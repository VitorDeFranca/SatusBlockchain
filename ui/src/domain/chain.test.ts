import { describe, expect, it } from 'vitest';
import type { Block } from '../api/types';
import { compareChains, describeComparison, linkOk, powPrefix, tamperBlock } from './chain';

/** Uma cadeia a partir de uma lista de hashes, com os elos certos. */
function chainOf(...hashes: string[]): Block[] {
  return hashes.map((hash, index) => ({
    index,
    timestamp: '2026-01-01T00:00:00Z',
    transactions: [],
    previousHash: index === 0 ? '0' : hashes[index - 1],
    nonce: index,
    hash,
  }));
}

describe('compareChains', () => {
  it('cadeias iguais: tudo em comum, sem fork', () => {
    const comparison = compareChains(chainOf('g', 'a1'), chainOf('g', 'a1'));

    expect(comparison.common).toBe(2);
    expect(comparison.forkIndex).toBeNull();
    expect(comparison.rows.map((row) => row.kind)).toEqual(['same', 'same']);
  });

  it('uma é prefixo da outra: só atraso, sem fork', () => {
    const comparison = compareChains(chainOf('g', 'a1', 'a2'), chainOf('g', 'a1'));

    expect(comparison.forkIndex).toBeNull();
    expect(comparison.rows.map((row) => row.kind)).toEqual(['same', 'same', 'onlyA']);
    expect(describeComparison(comparison, 'node1', 'node3', 3, 2)).toContain('node3 é um prefixo');
  });

  it('fork: o primeiro índice diferente é o ponto de fork', () => {
    const comparison = compareChains(chainOf('g', 'a1', 'a2'), chainOf('g', 'b1'));

    expect(comparison.common).toBe(1);
    expect(comparison.forkIndex).toBe(1);
    expect(comparison.rows.map((row) => row.kind)).toEqual(['same', 'fork', 'onlyA']);
    expect(describeComparison(comparison, 'node1', 'node3', 3, 2)).toBe(
      'Fork no bloco #1: 1 bloco em comum; depois disso, node1 tem 2 blocos próprios e node3, 1 bloco próprio. ' +
        'No sync vence o ramo mais longo; o outro vira órfão e as transações dele voltam para a mempool.',
    );
  });
});

describe('utilitários de bloco', () => {
  it('powPrefix destaca só os zeros exigidos pela dificuldade', () => {
    expect(powPrefix('000a12', 2)).toEqual(['00', '0a12']);
    expect(powPrefix('000a12', null)).toEqual(['000', 'a12']);
    expect(powPrefix('abc', 4)).toEqual(['', 'abc']);
  });

  it('linkOk confere o elo com o anterior (e o "0" do genesis)', () => {
    const chain = chainOf('g', 'a1');
    expect(linkOk(chain[0], undefined)).toBe(true);
    expect(linkOk(chain[1], chain[0])).toBe(true);
    expect(linkOk({ ...chain[1], previousHash: 'outro' }, chain[0])).toBe(false);
  });

  it('tamperBlock troca o conteúdo e mantém o hash declarado', () => {
    const block: Block = { ...chainOf('g', 'a1')[1], transactions: [{ from: 'alice', to: 'bob', amount: 10 }] };

    const byAmount = tamperBlock(block, 999, null);
    expect(byAmount.transactions[0].amount).toBe(999);
    expect(byAmount.hash).toBe(block.hash);
    expect(block.transactions[0].amount).toBe(10); // o original não muda

    expect(tamperBlock(block, null, 77).nonce).toBe(77);
  });
});
