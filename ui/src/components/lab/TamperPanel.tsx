import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { api } from '../../api/client';
import type { BlockInspection, NetworkSnapshot } from '../../api/types';
import { tamperBlock } from '../../domain/chain';
import type { NodeActions } from '../../hooks/useNodeActions';
import { InspectionResult } from '../BlockDetail';
import { Hash } from '../Hash';

interface Props {
  snapshot: NetworkSnapshot;
  actions: NodeActions;
  /** Vindo do explorador: "adulterar este bloco". */
  preset: { node: string; index: number } | null;
}

/**
 * Adulteração (o nó "bizantino" do laboratório): pegar um bloco válido, trocar o conteúdo e
 * manter o hash declarado. O nó de origem mostra o hash recalculado divergindo; entregue a
 * outro nó como um peer faria, o bloco é recusado (409). A defesa não é confiar no remetente:
 * é cada nó recalcular.
 *
 * Observação: o bloco passa pelo JSON do navegador. Um valor com zeros à direita (10.0) volta
 * como 10 e muda o hash — os valores criados pelo console são sempre números "limpos".
 */
export function TamperPanel({ snapshot, actions, preset }: Props) {
  const running = snapshot.nodes.filter((node) => node.state === 'running');
  const [sourceId, setSourceId] = useState(preset?.node ?? running[0]?.id ?? '');
  const [index, setIndex] = useState<number | null>(preset?.index ?? null);
  const [value, setValue] = useState('');
  const [targetId, setTargetId] = useState(running.find((node) => node.id !== sourceId)?.id ?? sourceId);
  const [inspection, setInspection] = useState<BlockInspection | null>(null);

  const chain = useQuery({ queryKey: ['chain', sourceId], queryFn: () => api.chain(sourceId), enabled: !!sourceId });
  const block = chain.data ? chain.data[index ?? chain.data.length - 1] : undefined;
  const difficulty = snapshot.nodes.find((node) => node.id === sourceId)?.difficulty ?? null;

  if (running.length === 0) return <p className="muted">Nenhum nó respondendo.</p>;

  // Bloco com transações: troca o valor da primeira. Sem transações (genesis, bloco vazio): o nonce.
  const editsAmount = !!block && block.transactions.length > 0;
  const fallback = block ? (editsAmount ? 999 : block.nonce + 1) : 0;
  const parsed = value.trim() === '' ? fallback : Number(value.replace(',', '.'));
  const tampered = block && Number.isFinite(parsed)
    ? tamperBlock(block, editsAmount ? parsed : null, editsAmount ? null : parsed)
    : null;

  async function verify() {
    if (!tampered) return;
    const result = await api.inspect(sourceId, tampered);
    setInspection(result.data);
  }

  return (
    <div className="tamper">
      <div className="tamper__controls">
        <label>
          Nó de origem
          <select value={sourceId} onChange={(e) => { setSourceId(e.target.value); setIndex(null); setInspection(null); }}>
            {running.map((node) => <option key={node.id} value={node.id}>{node.id}</option>)}
          </select>
        </label>
        <label>
          Bloco
          <select
            value={block?.index ?? ''}
            onChange={(e) => { setIndex(Number(e.target.value)); setInspection(null); setValue(''); }}
            disabled={!chain.data}
          >
            {(chain.data ?? []).map((b) => <option key={b.hash} value={b.index}>#{b.index}</option>)}
          </select>
        </label>
        <label>
          {editsAmount ? `Novo valor de ${block!.transactions[0].from} → ${block!.transactions[0].to}` : 'Novo nonce'}
          <input value={value} placeholder={String(fallback)} onChange={(e) => { setValue(e.target.value); setInspection(null); }}
            inputMode="decimal" />
        </label>
        <label>
          Entregar a
          <select value={targetId} onChange={(e) => setTargetId(e.target.value)}>
            {running.map((node) => <option key={node.id} value={node.id}>{node.id}</option>)}
          </select>
        </label>
      </div>

      {block && tampered ? (
        <>
          <p className="muted">
            Original: {editsAmount ? `${block.transactions[0].from} → ${block.transactions[0].to} (${block.transactions[0].amount})` : `nonce ${block.nonce}`}
            {' '}→ adulterado: {editsAmount ? `valor ${tampered.transactions[0].amount}` : `nonce ${tampered.nonce}`}.
            Hash declarado (mantido): <Hash value={block.hash} difficulty={difficulty} />
          </p>
          <div className="buttons">
            <button type="button" onClick={verify}>Verificar no {sourceId}</button>
            <button type="button" className="danger" disabled={!!actions.busy[targetId]} onClick={() => actions.deliver(targetId, tampered)}>
              Entregar a {targetId} (como um peer)
            </button>
          </div>
          {inspection ? <InspectionResult inspection={inspection} difficulty={difficulty} /> : null}
        </>
      ) : (
        <p className="muted">Carregando a cadeia…</p>
      )}
    </div>
  );
}
