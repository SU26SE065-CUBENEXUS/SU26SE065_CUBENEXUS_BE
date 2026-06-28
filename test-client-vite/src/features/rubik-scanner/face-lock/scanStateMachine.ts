import { FACE_ORDER, type FaceName, type FaceScanResult, type Grid3x3 } from '../types';
import { gridSimilarity } from '../cube-state/cubeStateBuilder';

export type ScanState = 'WAITING_FOR_FACE' | 'SCANNING_FACE' | 'FACE_LOCKED' | 'COMPLETED' | 'FAILED';

export type ScanSessionState = {
  state: ScanState;
  currentFace: FaceName;
  currentIndex: number;
  lockedFaces: Partial<Record<FaceName, FaceScanResult>>;
  statusText: string;
};

export function createInitialScanSessionState(): ScanSessionState {
  return {
    state: 'WAITING_FOR_FACE',
    currentFace: FACE_ORDER[0],
    currentIndex: 0,
    lockedFaces: {},
    statusText: 'Show face U and start scan.',
  };
}

export function lockFace(session: ScanSessionState, result: FaceScanResult): ScanSessionState {
  if (result.status !== 'PASSED' || !result.grid) {
    return { ...session, state: 'FAILED', statusText: result.reason ?? 'Face scan failed.' };
  }

  const duplicateSimilarity = maxSimilarity(result.grid, session.lockedFaces, session.currentFace);
  if (duplicateSimilarity >= 0.8) {
    return {
      ...session,
      state: 'FAILED',
      statusText: 'This face looks similar to a previous face.',
    };
  }

  const lockedFaces = { ...session.lockedFaces, [session.currentFace]: result };
  const completed = FACE_ORDER.every((face) => lockedFaces[face]?.grid);
  return {
    ...session,
    lockedFaces,
    state: completed ? 'COMPLETED' : 'FACE_LOCKED',
    statusText: completed ? 'Cube scan completed.' : `Face ${session.currentFace} locked.`,
  };
}

export function advanceFace(session: ScanSessionState): ScanSessionState {
  const nextIndex = Math.min(session.currentIndex + 1, FACE_ORDER.length - 1);
  return {
    ...session,
    currentIndex: nextIndex,
    currentFace: FACE_ORDER[nextIndex],
    state: 'WAITING_FOR_FACE',
    statusText: `Show face ${FACE_ORDER[nextIndex]} and start scan.`,
  };
}

function maxSimilarity(grid: Grid3x3, lockedFaces: Partial<Record<FaceName, FaceScanResult>>, currentFace: FaceName): number {
  return Math.max(
    0,
    ...Object.entries(lockedFaces)
      .filter(([face, result]) => face !== currentFace && result.grid)
      .map(([, result]) => gridSimilarity(grid, result.grid as Grid3x3)),
  );
}
