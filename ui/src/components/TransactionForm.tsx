import { type FormEvent, useState } from 'react';
import type { Transaction } from '../api/types';

interface Props {
  disabled: boolean;
  onSubmit: (transaction: Transaction) => void;
}

/**
 * Nova transação. A validação é a MESMA do nó (From e To preenchidos, Amount > 0) só para
 * avisar antes — quem decide continua sendo o nó, e uma recusa dele aparece no registro.
 */
export function TransactionForm({ disabled, onSubmit }: Props) {
  const [from, setFrom] = useState('alice');
  const [to, setTo] = useState('bob');
  const [amount, setAmount] = useState('10');
  const [error, setError] = useState<string | null>(null);

  function submit(event: FormEvent) {
    event.preventDefault();
    const value = Number(amount.replace(',', '.'));

    if (!from.trim() || !to.trim()) return setError('Informe origem e destino.');
    if (!Number.isFinite(value) || value <= 0) return setError('O valor precisa ser maior que zero.');

    setError(null);
    onSubmit({ from: from.trim(), to: to.trim(), amount: value });
  }

  return (
    <form className="tx-form" onSubmit={submit} noValidate>
      <label>
        De
        <input value={from} onChange={(e) => setFrom(e.target.value)} disabled={disabled} />
      </label>
      <label>
        Para
        <input value={to} onChange={(e) => setTo(e.target.value)} disabled={disabled} />
      </label>
      <label>
        Valor
        <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" disabled={disabled} />
      </label>
      <button type="submit" disabled={disabled}>
        Enviar transação
      </button>
      {error ? (
        <p className="form-error" role="alert">
          {error}
        </p>
      ) : null}
    </form>
  );
}
