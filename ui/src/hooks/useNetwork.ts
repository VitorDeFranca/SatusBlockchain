import { useQuery } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { api } from '../api/client';
import type { NetworkSnapshot } from '../api/types';
import { grownNodes } from '../domain/network';

/** Intervalo do polling: um snapshot da rede inteira por pedido (o gateway agrega os nós). */
export const POLL_INTERVAL_MS = 2000;

const PULSE_MS = 1600;

/**
 * A rede, atualizada a cada 2 s, e os nós cuja cadeia acabou de crescer — é assim que a
 * propagação de um bloco "aparece" na tela sem eventos em tempo real: o pulso passa de nó em
 * nó conforme cada um recebe o bloco.
 */
export function useNetwork() {
  const previous = useRef<NetworkSnapshot | undefined>(undefined);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const [grown, setGrown] = useState<string[]>([]);

  const query = useQuery({
    queryKey: ['network'],
    queryFn: async () => {
      const next = await api.network();
      const ids = grownNodes(previous.current, next);
      previous.current = next;

      if (ids.length > 0) {
        setGrown(ids);
        clearTimeout(timer.current);
        timer.current = setTimeout(() => setGrown([]), PULSE_MS);
      }

      return next;
    },
    refetchInterval: POLL_INTERVAL_MS,
  });

  useEffect(() => () => clearTimeout(timer.current), []);

  return { ...query, grown };
}
