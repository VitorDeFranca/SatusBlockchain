import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TransactionForm } from './TransactionForm';

afterEach(cleanup);

function fill(label: string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

describe('TransactionForm', () => {
  it('envia a transação com o valor como número', () => {
    const onSubmit = vi.fn();
    render(<TransactionForm disabled={false} onSubmit={onSubmit} />);

    fill('De', ' carol ');
    fill('Para', 'dave');
    fill('Valor', '2,5');
    fireEvent.click(screen.getByRole('button', { name: 'Enviar transação' }));

    expect(onSubmit).toHaveBeenCalledWith({ from: 'carol', to: 'dave', amount: 2.5 });
  });

  it('mesma validação do nó: origem obrigatória e valor maior que zero', () => {
    const onSubmit = vi.fn();
    render(<TransactionForm disabled={false} onSubmit={onSubmit} />);

    fill('De', '');
    fireEvent.click(screen.getByRole('button', { name: 'Enviar transação' }));
    expect(screen.getByRole('alert').textContent).toContain('origem e destino');

    fill('De', 'alice');
    fill('Valor', '0');
    fireEvent.click(screen.getByRole('button', { name: 'Enviar transação' }));
    expect(screen.getByRole('alert').textContent).toContain('maior que zero');

    expect(onSubmit).not.toHaveBeenCalled();
  });
});
