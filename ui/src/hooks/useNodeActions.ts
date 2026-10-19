import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { api } from '../api/client';
import type { Block, NetworkSnapshot, NodeRole, StopMode, Transaction } from '../api/types';
import {
  type Activity,
  describeCreate,
  describeDelay,
  describeDelivery,
  describeMine,
  describePartition,
  describeRemove,
  describeReset,
  describeStart,
  describeStop,
  describeSync,
  describeSyncAll,
  describeTransaction,
} from '../domain/messages';

/** Chave do "ocupado" do botão de adicionar nó (as demais são ids de nó). */
export const CREATE_KEY = '__create__';

/** Chave do "ocupado" das ações do laboratório sobre a rede inteira. */
export const LAB_KEY = '__lab__';

/**
 * As operações da tela. Cada uma: marca o nó como ocupado (o botão mostra "Minerando…"),
 * chama o gateway, registra o resultado em português e pede um snapshot novo — sem esperar
 * o próximo poll para ver o efeito. Devolve o registro, para quem encadeia passos (o roteiro
 * de fork do laboratório) saber se deu certo.
 */
export function useNodeActions(snapshot: NetworkSnapshot | undefined, log: (activity: Activity) => void) {
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState<Record<string, string>>({});

  async function run(key: string, label: string, operation: () => Promise<Activity>): Promise<Activity> {
    setBusy((current) => ({ ...current, [key]: label }));
    try {
      const activity = await operation();
      log(activity);
      return activity;
    } finally {
      setBusy((current) => {
        const next = { ...current };
        delete next[key];
        return next;
      });
      await queryClient.invalidateQueries();
    }
  }

  /** Os relatórios de sync falam em URLs; a tela, em ids. */
  function nameOf(url: string): string {
    const nodes = snapshot?.nodes ?? [];
    const direct = nodes.find((node) => node.url === url || node.advertiseUrl === url);
    if (direct) return direct.id;
    const viaPeers = nodes.flatMap((node) => node.peers).find((peer) => peer.url === url && peer.nodeId);
    return viaPeers?.nodeId ?? url;
  }

  return {
    busy,
    transaction: (id: string, transaction: Transaction) =>
      run(id, 'Enviando transação…', async () => describeTransaction(id, transaction, await api.postTransaction(id, transaction))),
    mine: (id: string) => run(id, 'Minerando…', async () => describeMine(id, await api.mine(id))),
    sync: (id: string) => run(id, 'Sincronizando…', async () => describeSync(id, await api.sync(id), nameOf)),
    create: (hostPort?: number) => run(CREATE_KEY, 'Criando nó…', async () => describeCreate(await api.createNode(hostPort))),
    stop: (id: string, mode: StopMode) =>
      run(id, mode === 'graceful' ? 'Parando…' : 'Derrubando…', async () => describeStop(id, mode, await api.stopNode(id, mode))),
    start: (id: string, role: NodeRole) => run(id, 'Religando…', async () => describeStart(id, role, await api.startNode(id))),
    remove: (id: string) => run(id, 'Removendo…', async () => describeRemove(id, await api.removeNode(id))),

    // ---- laboratório (etapa 12) ----
    partition: (groups: Record<string, string>) =>
      run(LAB_KEY, 'Aplicando partição…', async () => describePartition(groups, await api.applyPartition(groups))),
    resetLab: () => run(LAB_KEY, 'Removendo falhas…', async () => describeReset(await api.resetLab())),
    syncAll: () => run(LAB_KEY, 'Sincronizando todos…', async () => describeSyncAll(await api.syncAll(), nameOf)),
    delay: (id: string, ms: number) => run(id, 'Aplicando atraso…', async () => describeDelay(id, ms, await api.setDelay(id, ms))),
    deliver: (id: string, block: Block) =>
      run(id, 'Entregando bloco…', async () => describeDelivery(id, block, await api.deliverBlock(id, block))),
  };
}

export type NodeActions = ReturnType<typeof useNodeActions>;
