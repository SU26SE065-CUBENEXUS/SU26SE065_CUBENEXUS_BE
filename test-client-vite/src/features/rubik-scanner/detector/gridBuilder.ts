import type { Box, FrameDetectionResult, Grid3x3, StickerDetection } from '../types';

export function buildGridFromDetections(detections: StickerDetection[], frameWidth: number, frameHeight: number): FrameDetectionResult {
  const filtered = detections.filter((detection) => detection.confidence >= 0.35);
  if (filtered.length < 9) {
    return { ok: false, averageConfidence: 0, detectedStickers: filtered.length, reason: 'Need 9 stickers.' };
  }

  const selected = selectBestNine(filtered, frameWidth, frameHeight);
  if (selected.length !== 9) {
    return { ok: false, averageConfidence: 0, detectedStickers: filtered.length, reason: 'Need a stable 3x3 grid.' };
  }

  selected.sort((a, b) => centerY(a.box) - centerY(b.box));
  const rows = [selected.slice(0, 3), selected.slice(3, 6), selected.slice(6, 9)];
  const grid = [] as unknown as Grid3x3;
  const confidenceMatrix: number[][] = [];
  const orderedBoxes: Box[] = [];

  rows.forEach((row, rowIndex) => {
    const sorted = row.sort((a, b) => centerX(a.box) - centerX(b.box));
    grid[rowIndex] = sorted.map((detection) => detection.color) as Grid3x3[number];
    confidenceMatrix[rowIndex] = sorted.map((detection) => detection.confidence);
    orderedBoxes.push(...sorted.map((detection) => detection.box));
  });

  const averageConfidence = confidenceMatrix.flat().reduce((sum, value) => sum + value, 0) / 9;
  return { ok: true, grid, confidenceMatrix, orderedBoxes, averageConfidence, detectedStickers: filtered.length };
}

function selectBestNine(detections: StickerDetection[], frameWidth: number, frameHeight: number): StickerDetection[] {
  const frameCenterX = frameWidth / 2;
  const frameCenterY = frameHeight / 2;
  return [...detections]
    .sort((a, b) => {
      const rankA = a.confidence - centerDistance(a.box, frameCenterX, frameCenterY) / 10000;
      const rankB = b.confidence - centerDistance(b.box, frameCenterX, frameCenterY) / 10000;
      return rankB - rankA;
    })
    .slice(0, 9);
}

function centerX(box: Box): number {
  return (box.x1 + box.x2) / 2;
}

function centerY(box: Box): number {
  return (box.y1 + box.y2) / 2;
}

function centerDistance(box: Box, x: number, y: number): number {
  return Math.abs(centerX(box) - x) + Math.abs(centerY(box) - y);
}
