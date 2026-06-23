import type { FaceName, FaceScanResult, FrameDetectionResult, Grid3x3, RubikColor } from '../types';

type Options = {
  faceName: FaceName;
  scanSeconds?: number;
  minValidFrames?: number;
  cellMajorityThreshold?: number;
  faceStabilityThreshold?: number;
};

export class FaceConsensusBuffer {
  private startedAt = 0;
  private frames: FrameDetectionResult[] = [];
  private readonly options: Required<Options>;

  constructor(options: Options) {
    this.options = {
      scanSeconds: 5,
      minValidFrames: 12,
      cellMajorityThreshold: 0.6,
      faceStabilityThreshold: 0.7,
      ...options,
    };
  }

  start(now = performance.now()): void {
    this.startedAt = now;
    this.frames = [];
  }

  add(frame: FrameDetectionResult): void {
    if (frame.ok && frame.grid && frame.confidenceMatrix) {
      this.frames.push(frame);
    }
  }

  elapsed(now = performance.now()): number {
    return (now - this.startedAt) / 1000;
  }

  isFinished(now = performance.now()): boolean {
    return this.startedAt > 0 && this.elapsed(now) >= this.options.scanSeconds;
  }

  finalize(): FaceScanResult {
    const validFrames = this.frames.length;
    if (validFrames < this.options.minValidFrames) {
      return this.fail('LOW_VALID_FRAME_COUNT', validFrames);
    }

    const grid = Array.from({ length: 3 }, () => Array.from({ length: 3 }, () => 'white')) as Grid3x3;
    const confidences = Array.from({ length: 3 }, () => Array.from({ length: 3 }, () => 0));
    const stabilityScores: number[] = [];

    for (let row = 0; row < 3; row += 1) {
      for (let col = 0; col < 3; col += 1) {
        const votes = new Map<RubikColor, number>();
        this.frames.forEach((frame) => {
          const color = frame.grid?.[row]?.[col];
          if (color) {
            votes.set(color, (votes.get(color) ?? 0) + 1);
          }
        });

        const winner = [...votes.entries()].sort((a, b) => b[1] - a[1])[0];
        if (!winner) {
          return this.fail('MISSING_CELL_VOTES', validFrames);
        }

        const [color, count] = winner;
        const ratio = count / validFrames;
        if (ratio < this.options.cellMajorityThreshold) {
          return this.fail('LOW_STABILITY', validFrames);
        }

        grid[row][col] = color;
        confidences[row][col] = this.meanCellConfidence(row, col, color);
        stabilityScores.push(ratio);
      }
    }

    const overallConfidence = stabilityScores.reduce((sum, score) => sum + score, 0) / stabilityScores.length;
    if (overallConfidence < this.options.faceStabilityThreshold) {
      return this.fail('LOW_FACE_STABILITY', validFrames, overallConfidence);
    }

    return {
      status: 'PASSED',
      faceName: this.options.faceName,
      grid,
      cellConfidences: confidences,
      overallConfidence,
      validFrames,
    };
  }

  private meanCellConfidence(row: number, col: number, color: RubikColor): number {
    const values = this.frames
      .filter((frame) => frame.grid?.[row]?.[col] === color)
      .map((frame) => frame.confidenceMatrix?.[row]?.[col] ?? 0);
    return values.length === 0 ? 0 : values.reduce((sum, value) => sum + value, 0) / values.length;
  }

  private fail(reason: string, validFrames: number, overallConfidence = 0): FaceScanResult {
    return {
      status: 'FAILED',
      faceName: this.options.faceName,
      overallConfidence,
      validFrames,
      reason,
    };
  }
}
