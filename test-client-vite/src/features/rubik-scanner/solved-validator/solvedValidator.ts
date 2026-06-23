import type { CubeState } from '../types';
import { validateCubeColorCounts } from '../cube-state/cubeStateBuilder';

export function validateSolvedState(cubeState: CubeState): { ok: boolean; reason?: string } {
  const counts = validateCubeColorCounts(cubeState);
  if (!counts.ok) {
    return counts;
  }

  for (const [face, grid] of Object.entries(cubeState)) {
    const center = grid[1][1];
    if (!grid.flat().every((color) => color === center)) {
      return { ok: false, reason: `Face ${face} is not solved.` };
    }
  }

  return { ok: true };
}
