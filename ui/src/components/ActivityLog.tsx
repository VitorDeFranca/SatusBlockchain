import type { Activity } from '../domain/messages';

export interface LoggedActivity extends Activity {
  id: number;
  at: Date;
}

/** O que foi feito pela tela e o que a rede respondeu, do mais recente ao mais antigo. */
export function ActivityLog({ entries }: { entries: LoggedActivity[] }) {
  return (
    <section className="log" aria-label="Registro de operações">
      <h2>Registro de operações</h2>
      {entries.length === 0 ? (
        <p className="muted">As operações feitas por aqui aparecem nesta lista, com o que cada nó respondeu.</p>
      ) : (
        <ol aria-live="polite">
          {entries.map((entry) => (
            <li key={entry.id} className={entry.ok ? 'log__ok' : 'log__fail'}>
              <time dateTime={entry.at.toISOString()}>{entry.at.toLocaleTimeString('pt-BR')}</time>
              <span className="log__icon" aria-label={entry.ok ? 'sucesso' : 'falha'}>
                {entry.ok ? '✓' : '✕'}
              </span>
              <strong>{entry.action}</strong>
              <span className="log__node">{entry.node}</span>
              <span className="log__message">{entry.message}</span>
              {entry.details && entry.details.length > 0 ? (
                <details>
                  <summary>detalhes</summary>
                  <ul>
                    {entry.details.map((detail) => (
                      <li key={detail}>{detail}</li>
                    ))}
                  </ul>
                </details>
              ) : null}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
