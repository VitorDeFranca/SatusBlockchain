import { powPrefix } from '../domain/chain';

interface Props {
  value: string;
  /** Dificuldade do nó: quantos zeros iniciais destacar como "o trabalho" do PoW. */
  difficulty: number | null;
  /** Mostrar o hash inteiro (detalhe) ou só o começo (listas). */
  full?: boolean;
}

const SHORT = 14;

/** Um hash com os zeros do Proof of Work em destaque — o "trabalho" visível no próprio hash. */
export function Hash({ value, difficulty, full = false }: Props) {
  const [zeros, rest] = powPrefix(value, difficulty);
  const shown = full ? rest : rest.slice(0, Math.max(0, SHORT - zeros.length));
  const cut = !full && zeros.length + rest.length > SHORT;

  return (
    <code className="hash" title={value}>
      <span className="hash__pow">{zeros}</span>
      {shown}
      {cut ? '…' : ''}
    </code>
  );
}
