import type { Block } from '../api/types';
import { plural } from './network';

/** Situação de uma posição (índice) ao comparar a cadeia de dois nós. */
export type RowKind = 'same' | 'fork' | 'branch' | 'onlyA' | 'onlyB';

export interface ComparisonRow {
  index: number;
  a: Block | null;
  b: Block | null;
  kind: RowKind;
}

export interface ChainComparison {
  rows: ComparisonRow[];
  /** Blocos iguais desde o genesis. */
  common: number;
  /** Primeiro índice em que as duas cadeias têm blocos diferentes (null = uma é prefixo da outra). */
  forkIndex: number | null;
}

/**
 * Alinha duas cadeias por índice. Enquanto os hashes coincidem, é história comum; no primeiro
 * índice em que diferem está o PONTO DE FORK — dali em diante cada nó tem um ramo próprio, e
 * no sync o ramo mais curto vira órfão.
 */
export function compareChains(a: Block[], b: Block[]): ChainComparison {
  let common = 0;
  while (common < a.length && common < b.length && a[common].hash === b[common].hash) common++;

  const forkIndex = common < Math.min(a.length, b.length) ? common : null;

  const rows: ComparisonRow[] = [];
  for (let index = 0; index < Math.max(a.length, b.length); index++) {
    const left = a[index] ?? null;
    const right = b[index] ?? null;
    const kind: RowKind =
      left && right ? (index < common ? 'same' : index === forkIndex ? 'fork' : 'branch') : left ? 'onlyA' : 'onlyB';
    rows.push({ index, a: left, b: right, kind });
  }

  return { rows, common, forkIndex };
}

/** A comparação em uma frase. */
export function describeComparison(comparison: ChainComparison, nameA: string, nameB: string, lengthA: number, lengthB: number): string {
  if (comparison.forkIndex === null) {
    if (lengthA === lengthB) return `Cadeias idênticas: ${plural(lengthA, 'bloco', 'blocos')}.`;
    const [shorter, longer, diff] = lengthA < lengthB ? [nameA, nameB, lengthB - lengthA] : [nameB, nameA, lengthA - lengthB];
    return `A cadeia do ${shorter} é um prefixo da do ${longer}: ele só está atrasado (${plural(diff, 'bloco', 'blocos')}). Sincronizar resolve sem perder nada.`;
  }

  const ownA = lengthA - comparison.common;
  const ownB = lengthB - comparison.common;
  return (
    `Fork no bloco #${comparison.forkIndex}: ${plural(comparison.common, 'bloco', 'blocos')} em comum; depois disso, ` +
    `${nameA} tem ${plural(ownA, 'bloco próprio', 'blocos próprios')} e ${nameB}, ${plural(ownB, 'bloco próprio', 'blocos próprios')}. ` +
    'No sync vence o ramo mais longo; o outro vira órfão e as transações dele voltam para a mempool.'
  );
}

/**
 * Separa os zeros do Proof of Work do resto do hash, para destacá-los. Com a dificuldade
 * conhecida, destaca exatamente os zeros exigidos; sem ela, todos os zeros iniciais.
 */
export function powPrefix(hash: string, difficulty: number | null): [string, string] {
  let zeros = 0;
  while (zeros < hash.length && hash[zeros] === '0' && (difficulty === null || zeros < difficulty)) zeros++;
  return [hash.slice(0, zeros), hash.slice(zeros)];
}

/** O elo do bloco confere? (genesis aponta para "0"; os demais, para o hash do anterior.) */
export function linkOk(block: Block, previous: Block | undefined): boolean {
  return block.index === 0 ? block.previousHash === '0' : previous?.hash === block.previousHash;
}

/**
 * Uma cópia do bloco com o conteúdo trocado e o hash DECLARADO mantido — é o que um nó
 * adulterador mandaria: o conteúdo diz uma coisa, o hash, outra.
 */
export function tamperBlock(block: Block, amount: number | null, nonce: number | null): Block {
  const transactions =
    amount !== null && block.transactions.length > 0
      ? [{ ...block.transactions[0], amount }, ...block.transactions.slice(1)]
      : block.transactions;
  return { ...block, transactions, nonce: nonce ?? block.nonce };
}
