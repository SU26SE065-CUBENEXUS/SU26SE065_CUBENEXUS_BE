import { RUBIK_COLORS, type Box, type RubikColor, type StickerDetection } from '../types';

type PostprocessArgs = {
  output: Float32Array;
  dims: readonly number[];
  confidenceThreshold: number;
  iouThreshold: number;
  scale: number;
  padX: number;
  padY: number;
};

export function postprocessYoloOutput(args: PostprocessArgs): StickerDetection[] {
  const [batch, channels, anchors] = normalizeDims(args.dims);
  if (batch !== 1 || channels < 10) {
    throw new Error(`Unsupported YOLO output shape: ${args.dims.join('x')}`);
  }

  const candidates: StickerDetection[] = [];
  for (let anchor = 0; anchor < anchors; anchor += 1) {
    const cx = args.output[anchor];
    const cy = args.output[anchors + anchor];
    const width = args.output[2 * anchors + anchor];
    const height = args.output[3 * anchors + anchor];

    let classId = 0;
    let confidence = 0;
    for (let classIndex = 0; classIndex < RUBIK_COLORS.length; classIndex += 1) {
      const score = args.output[(4 + classIndex) * anchors + anchor];
      if (score > confidence) {
        confidence = score;
        classId = classIndex;
      }
    }

    if (confidence < args.confidenceThreshold) {
      continue;
    }

    const x1 = (cx - width / 2 - args.padX) / args.scale;
    const y1 = (cy - height / 2 - args.padY) / args.scale;
    const x2 = (cx + width / 2 - args.padX) / args.scale;
    const y2 = (cy + height / 2 - args.padY) / args.scale;
    candidates.push({
      box: { x1, y1, x2, y2 },
      confidence,
      classId,
      color: RUBIK_COLORS[classId] as RubikColor,
    });
  }

  return nonMaxSuppression(candidates, args.iouThreshold);
}

function normalizeDims(dims: readonly number[]): [number, number, number] {
  if (dims.length !== 3) {
    throw new Error(`Expected YOLO output dims [1, channels, anchors], got ${dims.join('x')}`);
  }
  return [dims[0], dims[1], dims[2]];
}

function nonMaxSuppression(detections: StickerDetection[], iouThreshold: number): StickerDetection[] {
  const sorted = [...detections].sort((a, b) => b.confidence - a.confidence);
  const kept: StickerDetection[] = [];
  for (const detection of sorted) {
    if (kept.every((existing) => iou(existing.box, detection.box) < iouThreshold)) {
      kept.push(detection);
    }
  }
  return kept;
}

function iou(a: Box, b: Box): number {
  const x1 = Math.max(a.x1, b.x1);
  const y1 = Math.max(a.y1, b.y1);
  const x2 = Math.min(a.x2, b.x2);
  const y2 = Math.min(a.y2, b.y2);
  const intersection = Math.max(0, x2 - x1) * Math.max(0, y2 - y1);
  const areaA = Math.max(0, a.x2 - a.x1) * Math.max(0, a.y2 - a.y1);
  const areaB = Math.max(0, b.x2 - b.x1) * Math.max(0, b.y2 - b.y1);
  return intersection / Math.max(1, areaA + areaB - intersection);
}
