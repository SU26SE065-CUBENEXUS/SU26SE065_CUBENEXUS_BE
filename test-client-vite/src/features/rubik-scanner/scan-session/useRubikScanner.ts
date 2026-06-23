import { useCallback, useMemo, useRef, useState } from 'react';
import type { FaceName, FaceScanResult, FrameDetectionResult, Grid3x3, ScanMetadata } from '../types';
import { buildCubeState } from '../cube-state/cubeStateBuilder';
import { FaceConsensusBuffer } from '../consensus/FaceConsensusBuffer';
import { YoloRubikDetector } from '../detector/yoloDetector';
import { advanceFace, createInitialScanSessionState, lockFace, type ScanSessionState } from '../face-lock/scanStateMachine';
import { createOnnxSession, type OnnxRuntimeSession } from '../onnx-runtime/createOnnxSession';

type Options = {
  modelUrl?: string;
  modelVersion?: string;
  scanSeconds?: number;
  inferEvery?: number;
  previewThrottleMs?: number;
};

export function useRubikScanner(options: Options = {}) {
  const modelUrl = options.modelUrl ?? '/models/rubik-yolo/best.onnx';
  const modelVersion = options.modelVersion ?? 'rubik-yolo-best.onnx';
  const scanSeconds = options.scanSeconds ?? 5;
  const inferEvery = Math.max(1, options.inferEvery ?? 2);
  const previewThrottleMs = Math.max(0, options.previewThrottleMs ?? 250);
  const [session, setSession] = useState<ScanSessionState>(() => createInitialScanSessionState());
  const [runtime, setRuntime] = useState<OnnxRuntimeSession | null>(null);
  const [modelStatus, setModelStatus] = useState<'idle' | 'loading' | 'ready' | 'failed'>('idle');
  const [lastFrame, setLastFrame] = useState<FrameDetectionResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const detectorRef = useRef<YoloRubikDetector | null>(null);
  const consensusRef = useRef<FaceConsensusBuffer | null>(null);
  const isTickingRef = useRef(false);
  const frameCounterRef = useRef(0);
  const lastPreviewAtRef = useRef(0);

  const loadModel = useCallback(async () => {
    setModelStatus('loading');
    setError(null);
    try {
      const created = await createOnnxSession(modelUrl);
      setRuntime(created);
      detectorRef.current = new YoloRubikDetector(created.session, created.session.inputNames[0]);
      setModelStatus('ready');
    } catch (err) {
      setModelStatus('failed');
      setError(err instanceof Error ? err.message : String(err));
    }
  }, [modelUrl]);

  const startFaceScan = useCallback(() => {
    if (!detectorRef.current) {
      setError('Load ONNX model before scanning.');
      return;
    }

    const buffer = new FaceConsensusBuffer({ faceName: session.currentFace, scanSeconds });
    buffer.start();
    consensusRef.current = buffer;
    frameCounterRef.current = 0;
    lastPreviewAtRef.current = 0;
    setLastFrame(null);
    setError(null);
    setSession((current) => ({ ...current, state: 'SCANNING_FACE', statusText: `Scanning face ${current.currentFace}.` }));
  }, [scanSeconds, session.currentFace]);

  const tick = useCallback(async (video: HTMLVideoElement | null) => {
    if (!video || !detectorRef.current || !consensusRef.current || isTickingRef.current) {
      return;
    }
    isTickingRef.current = true;
    try {
      frameCounterRef.current += 1;
      if ((frameCounterRef.current - 1) % inferEvery !== 0) {
        return;
      }

      const frame = await detectorRef.current.detect(video);
      const now = performance.now();
      if (now - lastPreviewAtRef.current >= previewThrottleMs) {
        setLastFrame(frame);
        lastPreviewAtRef.current = now;
      }
      consensusRef.current.add(frame);
      if (consensusRef.current.isFinished()) {
        const result = consensusRef.current.finalize();
        consensusRef.current = null;
        setLastFrame(frame);
        setSession((current) => lockFace(current, result));
      }
    } catch (err) {
      consensusRef.current = null;
      setError(err instanceof Error ? err.message : String(err));
      setSession((current) => ({ ...current, state: 'FAILED', statusText: 'Face scan failed while running inference.' }));
    } finally {
      isTickingRef.current = false;
    }
  }, [inferEvery, previewThrottleMs]);

  const nextFace = useCallback(() => {
    setSession((current) => advanceFace(current));
  }, []);

  const reset = useCallback(() => {
    consensusRef.current = null;
    frameCounterRef.current = 0;
    lastPreviewAtRef.current = 0;
    setLastFrame(null);
    setError(null);
    setSession(createInitialScanSessionState());
  }, []);

  const cubeState = useMemo(() => {
    const faces: Partial<Record<FaceName, Grid3x3>> = {};
    Object.entries(session.lockedFaces).forEach(([face, result]) => {
      if ((result as FaceScanResult).grid) {
        faces[face as FaceName] = (result as FaceScanResult).grid;
      }
    });
    return buildCubeState(faces);
  }, [session.lockedFaces]);

  const metadata = useCallback((deviceLabel = ''): ScanMetadata => ({
    scannerVersion: 'web-scanner-0.1.0',
    modelVersion,
    runtime: 'onnxruntime-web',
    executionProvider: runtime?.executionProvider ?? 'not-loaded',
    overallConfidence: Object.values(session.lockedFaces).reduce((sum, face) => sum + (face?.overallConfidence ?? 0), 0) / Math.max(1, Object.values(session.lockedFaces).length),
    validFrames: Object.values(session.lockedFaces).reduce((sum, face) => sum + (face?.validFrames ?? 0), 0),
    durationMs: scanSeconds * 1000,
    deviceLabel,
    scannedAt: new Date().toISOString(),
  }), [modelVersion, runtime?.executionProvider, scanSeconds, session.lockedFaces]);

  return {
    session,
    cubeState,
    metadata,
    modelStatus,
    lastFrame,
    error,
    loadModel,
    startFaceScan,
    tick,
    nextFace,
    reset,
    executionProvider: runtime?.executionProvider ?? 'not-loaded',
  };
}
