import { FACE_ORDER, RUBIK_COLORS, type CubeState, type FaceName, type Grid3x3 } from '../types';

export function buildCubeState(faces: Partial<Record<FaceName, Grid3x3>>): CubeState | null {
  const complete = {} as CubeState;
  for (const face of FACE_ORDER) {
    const grid = faces[face];
    if (!grid) {
      return null;
    }
    complete[face] = grid;
  }
  return complete;
}

export function flattenGrid(grid: Grid3x3): string[] {
  return grid.flat();
}

export function gridSimilarity(a: Grid3x3, b: Grid3x3): number {
  const flatA = flattenGrid(a);
  const flatB = flattenGrid(b);
  let same = 0;
  for (let index = 0; index < 9; index += 1) {
    if (flatA[index] === flatB[index]) {
      same += 1;
    }
  }
  return same / 9;
}

export function summarizeColorCounts(cubeState: Partial<Record<FaceName, Grid3x3>>): Record<string, number> {
  const counts = Object.fromEntries(RUBIK_COLORS.map((color) => [color, 0]));
  Object.values(cubeState).forEach((grid) => {
    grid?.flat().forEach((color) => {
      counts[color] = (counts[color] ?? 0) + 1;
    });
  });
  return counts;
}

export function validateCubeColorCounts(cubeState: CubeState): { ok: boolean; reason?: string; colorCounts: Record<string, number> } {
  const colorCounts = summarizeColorCounts(cubeState);
  for (const color of RUBIK_COLORS) {
    if (colorCounts[color] !== 9) {
      return { ok: false, reason: `Expected 9 ${color} stickers.`, colorCounts };
    }
  }
  return { ok: true, colorCounts };
}
