export const RUBIK_COLORS = ['white', 'yellow', 'red', 'orange', 'blue', 'green'] as const;
export const FACE_ORDER = ['U', 'R', 'F', 'D', 'L', 'B'] as const;

export type RubikColor = (typeof RUBIK_COLORS)[number];
export type FaceName = (typeof FACE_ORDER)[number];
export type Grid3x3 = [[RubikColor, RubikColor, RubikColor], [RubikColor, RubikColor, RubikColor], [RubikColor, RubikColor, RubikColor]];
export type CubeState = Record<FaceName, Grid3x3>;

export type Box = {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
};

export type StickerDetection = {
  box: Box;
  confidence: number;
  classId: number;
  color: RubikColor;
};

export type FrameDetectionResult = {
  ok: boolean;
  grid?: Grid3x3;
  confidenceMatrix?: number[][];
  orderedBoxes?: Box[];
  averageConfidence: number;
  detectedStickers: number;
  reason?: string;
};

export type FaceScanResult = {
  status: 'PASSED' | 'FAILED';
  faceName: FaceName;
  grid?: Grid3x3;
  cellConfidences?: number[][];
  overallConfidence: number;
  validFrames: number;
  reason?: string;
  possibleDuplicate?: boolean;
  duplicateSimilarity?: number;
};

export type ScanMetadata = {
  scannerVersion: string;
  modelVersion: string;
  runtime: 'onnxruntime-web';
  executionProvider: string;
  overallConfidence: number;
  validFrames: number;
  durationMs: number;
  deviceLabel?: string;
  scannedAt: string;
};

export type CubeScanValidationResponse = {
  message: string;
  matchId: string;
  playerId: string;
  validationType: 'SCRAMBLE_CHECK' | 'FINISH_CHECK';
  status: 'PASSED' | 'FAILED' | 'NEEDS_REVIEW';
  matchStatus: string;
  isValidCubeState: boolean;
  isScrambleMatched?: boolean;
  isSolved?: boolean;
  reason?: string;
  missing: string[];
  colorCounts: Record<string, number>;
  createdAt: string;
};

export type AiRubikHealthResponse = {
  status: string;
  serviceName: string;
  modelPath: string;
  modelExists: boolean;
  modelVersion: string;
  modelLoaded: boolean;
};

export type AiRubikCheckResponse = {
  matchId: string;
  playerId: string;
  checkType: string;
  status: string;
  confidence: number;
  detectedCube: boolean;
  detectedStickers: number;
  grid3x3?: string[][] | null;
  reason?: string | null;
  modelVersion: string;
  modelLoaded: boolean;
  evidenceImageUrl?: string | null;
  expectedScramble?: string | null;
  detectedState?: string | null;
  isScrambleMatched?: boolean | null;
  isSolved?: boolean | null;
  createdAt: string;
};

export type AiRubikScannerSticker = {
  color: string;
  confidence: number;
  bbox: [number, number, number, number];
};

export type AiRubikScannerFace = {
  centerColor: string;
  grid3x3: string[][];
  stickers: AiRubikScannerSticker[];
  overallConfidence: number;
  validFrames: number;
  capturedAt: string;
};

export type AiRubikScannerPreviewResponse = {
  status: string;
  scannerState: 'POSITION_FACE' | 'SCANNING' | 'STABLE' | 'ACCEPTED' | 'DUPLICATE_FACE' | 'RETRY' | 'AI_BUSY' | 'AI_UNAVAILABLE' | 'CAMERA_ERROR';
  scanSessionId: string;
  scanGeneration: number;
  requestId?: string | null;
  targetFaceIndex: number;
  requestedFaceIndex: number;
  requestedFaceLabel: string;
  centerColor?: string | null;
  grid3x3?: string[][] | null;
  stickers: AiRubikScannerSticker[];
  detectedStickers: number;
  confidence: number;
  inferMs: number;
  decodeMs: number;
  preprocessMs: number;
  postprocessMs: number;
  totalMs: number;
  stableObservationCount: number;
  requiredStableObservations: number;
  modelVersion: string;
  reason?: string | null;
};

export type AiRubikScannerSessionResponse = {
  sessionId: string;
  status: 'IN_PROGRESS' | 'COMPLETED';
  scannerState: 'POSITION_FACE' | 'SCANNING' | 'STABLE' | 'ACCEPTED' | 'DUPLICATE_FACE' | 'RETRY' | 'AI_BUSY' | 'AI_UNAVAILABLE' | 'CAMERA_ERROR';
  message: string;
  scanGeneration: number;
  requestedFaceIndex: number;
  requestedFaceLabel: string;
  capturedFaceCount: number;
  rawStickerCount: number;
  orientationResolved: boolean;
  modelVersion: string;
  startedAt: string;
  completedAt?: string | null;
  faces: AiRubikScannerFace[];
  rawStickerState: string[];
  lastFaceScan?: AiRubikScannerFace | null;
  lastScanStatus?: string | null;
  lastScanReason?: string | null;
};
