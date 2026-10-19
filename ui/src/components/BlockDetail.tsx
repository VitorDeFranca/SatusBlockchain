import { useState } from 'react';
import { api } from '../api/client';
import type { Block, BlockInspection } from '../api/types';
import { linkOk } from '../domain/chain';
import { Hash } from './Hash';

interface Props {
  block: Block;
  /** O nó de quem é a cadeia (é ele que verifica o bloco). */
  owner: string;
  difficulty: number | null;
  previous: Block | undefined;
  onTamper: (owner: string, index: number) => void;
}

/** Tudo sobre um bloco — e a verificação feita pelo próprio nó (hash recalculado, PoW, encaixe). */
export function BlockDetail({ block, owner, difficulty, previous, onTamper }: Props) {
  const [inspection, setInspection] = useState<BlockInspection | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [checking, setChecking] = useState(false);

  async function verify() {
    setChecking(true);
    const result = await api.inspect(owner, block);
    setChecking(false);
    setInspection(result.data);
    setError(result.ok ? null : result.error);
  }

  return (
    <div className="block-detail">
      <h3>
        Bloco #{block.index} <span className="muted">no {owner}</span>
      </h3>

      <dl className="facts">
        <dt>Hash</dt>
        <dd><Hash value={block.hash} difficulty={difficulty} full /></dd>
        <dt>Anterior</dt>
        <dd>
          {block.index === 0 ? <code>0</code> : <Hash value={block.previousHash} difficulty={difficulty} full />}{' '}
          <span className={linkOk(block, previous) ? 'ok-text' : 'bad-text'}>
            {linkOk(block, previous) ? '✓ elo confere' : '✕ elo quebrado'}
          </span>
        </dd>
        <dt>Nonce</dt>
        <dd>{block.nonce}</dd>
        <dt>Horário</dt>
        <dd>{new Date(block.timestamp).toLocaleString('pt-BR')}</dd>
      </dl>

      <h4>Transações ({block.transactions.length})</h4>
      {block.transactions.length === 0 ? (
        <p className="muted">{block.index === 0 ? 'O genesis não tem transações.' : 'Bloco vazio (minerado sem nada na mempool).'}</p>
      ) : (
        <table className="tx-table">
          <thead>
            <tr><th>De</th><th>Para</th><th>Valor</th></tr>
          </thead>
          <tbody>
            {block.transactions.map((tx, i) => (
              <tr key={i}><td>{tx.from}</td><td>{tx.to}</td><td>{tx.amount}</td></tr>
            ))}
          </tbody>
        </table>
      )}

      <div className="buttons">
        <button type="button" onClick={verify} disabled={checking}>
          {checking ? 'Verificando…' : `Verificar no ${owner}`}
        </button>
        <button type="button" onClick={() => onTamper(owner, block.index)}>Adulterar no laboratório</button>
      </div>

      {error ? <p className="form-error" role="alert">{error}</p> : null}
      {inspection ? <InspectionResult inspection={inspection} difficulty={difficulty} /> : null}
    </div>
  );
}

/** O veredito do nó, item a item — a mesma régua que ele usa para aceitar ou recusar blocos. */
export function InspectionResult({ inspection, difficulty }: { inspection: BlockInspection; difficulty: number | null }) {
  const check = (ok: boolean, text: string) => (
    <li className={ok ? 'ok-text' : 'bad-text'}>
      {ok ? '✓' : '✕'} {text}
    </li>
  );

  return (
    <div className="inspection" role="status">
      <p><strong>{inspection.verdict}</strong></p>
      <ul className="checks">
        {check(inspection.hashMatches, 'hash recalculado confere com o declarado')}
        {check(inspection.proofOfWork, 'Proof of Work válido para a dificuldade do nó')}
        {check(inspection.inChain || inspection.extendsTip, inspection.inChain ? 'já está na cadeia' : 'encaixa no topo da cadeia (índice e elo)')}
      </ul>
      <p className="muted">
        Recalculado pelo nó: <Hash value={inspection.recalculatedHash} difficulty={difficulty} full />
      </p>
    </div>
  );
}
