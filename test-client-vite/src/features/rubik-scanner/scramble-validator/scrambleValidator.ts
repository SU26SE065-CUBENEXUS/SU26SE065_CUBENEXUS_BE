import type { CubeState } from '../types';
import { validateCubeColorCounts } from '../cube-state/cubeStateBuilder';

export function validateScrambleScanReadiness(cubeState: CubeState): { ok: boolean; reason?: string } {
  const counts = validateCubeColorCounts(cubeState);
  if (!counts.ok) {
    return counts;
  }
  return { ok: true };
}
