import { useEffect, useRef, useState } from 'react';
import {
  fetchScannerTestHealth,
  observeScannerTestFrame,
  resetScannerTestSession,
  retryScannerTestFace,
  startScannerTestSession,
} from '../api/onlineScannerApi';
import { useCameraStream } from '../camera/useCameraStream';
import { runScannerBurst } from '../scanBurstControl';
import type {
  AiRubikHealthResponse,
  AiRubikScannerFace,
  AiRubikScannerPreviewResponse,
  AiRubikScannerSessionResponse,
} from '../types';

type Props = {
  backendUrl: string;
};

const CAPTURE_INTERVAL_MS = 300;
const MAX_SCAN_BURST_MS = 5000;
const SNAPSHOT_MAX_WIDTH = 640;
const SNAPSHOT_QUALITY = 0.76;

const COLOR_STYLE: Record<string, string> = {
  white: '#f8fafc',
  yellow: '#facc15',
  red: '#ef4444',
  orange: '#fb923c',
  blue: '#3b82f6',
  green: '#22c55e',
  unknown: '#475569',
};

const UI_MESSAGE: Record<string, string> = {
  POSITION_FACE: 'Hold one complete face inside the frame.',
  SCANNING: 'Keep all 9 stickers visible.',
  STABLE: 'Face detected. You may relax your hand.',
  ACCEPTED: 'Face accepted. Rotate to a different center color.',
  DUPLICATE_FACE: 'Face already scanned. Show another face.',
  RETRY: 'Detection unstable. Adjust the cube and retry.',
  AI_BUSY: 'AI service is busy. Retry shortly.',
  AI_UNAVAILABLE: 'AI service unavailable. Retry the current face.',
  CAMERA_ERROR: 'Camera error. Restart the camera and retry.',
};

export function RubikScannerPanel({ backendUrl }: Props) {
  const camera = useCameraStream();
  const [aiHealth, setAiHealth] = useState<AiRubikHealthResponse | null>(null);
  const [session, setSession] = useState<AiRubikScannerSessionResponse | null>(null);
  const [observation, setObservation] = useState<AiRubikScannerPreviewResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [scannerState, setScannerState] = useState<AiRubikScannerPreviewResponse['scannerState'] | AiRubikScannerSessionResponse['scannerState']>('POSITION_FACE');
  const [statusMessage, setStatusMessage] = useState(UI_MESSAGE.POSITION_FACE);
  const [isCheckingHealth, setIsCheckingHealth] = useState(false);
  const [isPreparingSession, setIsPreparingSession] = useState(false);
  const [isScanningFace, setIsScanningFace] = useState(false);
  const [renderMetrics, setRenderMetrics] = useState({
    requestsPerSecond: 0,
    maxConcurrentRequests: 0,
    avgInferenceMs: 0,
    p95InferenceMs: 0,
    acceptedFaceMs: 0,
  });

  const captureCanvasRef = useRef<HTMLCanvasElement | null>(null);
  const overlayCanvasRef = useRef<HTMLCanvasElement | null>(null);
  const requestInFlightRef = useRef(false);
  const maxConcurrentRequestsRef = useRef(0);
  const activeScanAbortRef = useRef<AbortController | null>(null);
  const activeScanIdentityRef = useRef<{ scanSessionId: string; scanGeneration: number; targetFaceIndex: number } | null>(null);
  const scanGenerationRef = useRef(0);
  const requestSamplesRef = useRef<number[]>([]);
  const inferenceSamplesRef = useRef<number[]>([]);

  useEffect(() => {
    void refreshHealth();
    const timer = window.setInterval(() => void refreshHealth(), 15000);
    return () => {
      window.clearInterval(timer);
      abortActiveScan();
    };
  }, [backendUrl]);

  useEffect(() => {
    if (camera.status !== 'ready') {
      abortActiveScan();
    }
  }, [camera.status]);

  useEffect(() => {
    const canvas = overlayCanvasRef.current;
    const video = camera.videoRef.current;
    if (!canvas || !video) {
      return;
    }

    canvas.width = video.videoWidth || 640;
    canvas.height = video.videoHeight || 480;
    const context = canvas.getContext('2d');
    if (!context) {
      return;
    }

    context.clearRect(0, 0, canvas.width, canvas.height);
    observation?.stickers.forEach((sticker, index) => {
      const [x1, y1, x2, y2] = sticker.bbox;
      context.strokeStyle = '#facc15';
      context.lineWidth = 2;
      context.strokeRect(x1, y1, x2 - x1, y2 - y1);
      context.fillStyle = 'rgba(17,24,39,0.82)';
      context.fillRect(x1, Math.max(0, y1 - 22), 102, 18);
      context.fillStyle = '#f8fafc';
      context.font = '12px Segoe UI';
      context.fillText(`${index + 1}. ${sticker.color}`, x1 + 4, Math.max(12, y1 - 8));
    });
  }, [camera.videoRef, observation]);

  async function refreshHealth() {
    setIsCheckingHealth(true);
    try {
      setAiHealth(await fetchScannerTestHealth(backendUrl));
      setError(null);
    } catch (err) {
      setAiHealth(null);
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setIsCheckingHealth(false);
    }
  }

  async function startScanSession() {
    abortActiveScan();
    setIsPreparingSession(true);
    try {
      const created = await startScannerTestSession(backendUrl);
      scanGenerationRef.current = created.scanGeneration;
      setSession(created);
      setObservation(null);
      setScannerState(created.scannerState);
      setStatusMessage('Session ready. Hold one face steady, then press Scan / Accept Next Face.');
      setError(null);
      resetMetrics();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
      setScannerState('AI_UNAVAILABLE');
      setStatusMessage(UI_MESSAGE.AI_UNAVAILABLE);
    } finally {
      setIsPreparingSession(false);
    }
  }

  async function scanCurrentFace() {
    if (camera.status !== 'ready') {
      setError('Start the camera before scanning.');
      setScannerState('CAMERA_ERROR');
      setStatusMessage(UI_MESSAGE.CAMERA_ERROR);
      return;
    }

    const currentSession = session ?? await startScannerTestSession(backendUrl);
    if (!session) {
      scanGenerationRef.current = currentSession.scanGeneration;
      setSession(currentSession);
    }

    const targetFaceIndex = currentSession.requestedFaceIndex;
    const scanGeneration = Math.max(scanGenerationRef.current, currentSession.scanGeneration) + 1;
    scanGenerationRef.current = scanGeneration;
    const scanIdentity = {
      scanSessionId: currentSession.sessionId,
      scanGeneration,
      targetFaceIndex,
    };

    abortActiveScan();
    const abortController = new AbortController();
    activeScanAbortRef.current = abortController;
    activeScanIdentityRef.current = scanIdentity;
    setIsScanningFace(true);
    setObservation(null);
    setScannerState('SCANNING');
    setStatusMessage(`Scanning ${currentSession.requestedFaceLabel}. Hold the cube still for 3 stable reads.`);
    setError(null);

    const startedAt = performance.now();
    requestSamplesRef.current = [];

    try {
      const result = await runScannerBurst({
        capture: async () => {
          requestInFlightRef.current = true;
          maxConcurrentRequestsRef.current = Math.max(maxConcurrentRequestsRef.current, 1);
          return captureSnapshot();
        },
        observe: async (snapshot) => {
          const tickStartedAt = performance.now();
          const nextObservation = await observeScannerTestFrame({
            backendUrl,
            sessionId: currentSession.sessionId,
            snapshot,
            ...scanIdentity,
            requestId: createRequestId(),
            signal: abortController.signal,
          });
          requestSamplesRef.current.push(1000 / Math.max(1, performance.now() - tickStartedAt));
          inferenceSamplesRef.current.push(nextObservation.totalMs);
          updateMetrics();
          return nextObservation;
        },
        delay,
        onObservation: (nextObservation) => {
          requestInFlightRef.current = false;
          if (!isObservationCurrent(nextObservation, scanIdentity)) {
            return;
          }
          setObservation(nextObservation);
          setScannerState(nextObservation.scannerState);
          setStatusMessage(nextObservation.reason || UI_MESSAGE[nextObservation.scannerState]);
          if (nextObservation.scannerState === 'ACCEPTED') {
            updateMetrics(performance.now() - startedAt);
            setSession((current) => applyAcceptedObservation(current, nextObservation, scanIdentity.scanGeneration));
          }
        },
        shouldAbort: () => abortController.signal.aborted,
        maxBurstMs: MAX_SCAN_BURST_MS,
        sampleIntervalMs: CAPTURE_INTERVAL_MS,
        now: () => performance.now(),
      });

      requestInFlightRef.current = false;
      if (result.reason === 'terminal' && result.observation?.scannerState === 'ACCEPTED') {
        setStatusMessage('Face accepted. Rotate to a different center color, then scan the next face.');
        return;
      }

      if (result.reason === 'timeout' && !abortController.signal.aborted) {
        setScannerState('RETRY');
        setStatusMessage(UI_MESSAGE.RETRY);
      }
    } catch (err) {
      requestInFlightRef.current = false;
      if (abortController.signal.aborted) {
        return;
      }
      setError(err instanceof Error ? err.message : String(err));
      setScannerState('AI_UNAVAILABLE');
      setStatusMessage(UI_MESSAGE.AI_UNAVAILABLE);
    } finally {
      requestInFlightRef.current = false;
      if (activeScanAbortRef.current === abortController) {
        activeScanAbortRef.current = null;
      }
      if (activeScanIdentityRef.current?.scanGeneration === scanIdentity.scanGeneration) {
        activeScanIdentityRef.current = null;
      }
      setIsScanningFace(false);
    }
  }

  async function retryCurrentFace() {
    abortActiveScan();
    if (!session) {
      return;
    }

    try {
      const updated = await retryScannerTestFace({ backendUrl, sessionId: session.sessionId });
      scanGenerationRef.current = updated.scanGeneration;
      setSession(updated);
      setObservation(null);
      setScannerState(updated.scannerState);
      setStatusMessage('Current face cleared. Re-center the same face and scan again.');
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  }

  async function resetEntireScan() {
    abortActiveScan();
    if (!session) {
      await startScanSession();
      return;
    }

    try {
      const updated = await resetScannerTestSession({ backendUrl, sessionId: session.sessionId });
      scanGenerationRef.current = updated.scanGeneration;
      setSession(updated);
      setObservation(null);
      setScannerState(updated.scannerState);
      setStatusMessage('Scan reset. Start again from face 1 when ready.');
      setError(null);
      resetMetrics();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  }

  async function copyResultJson() {
    if (!session) {
      return;
    }
    await navigator.clipboard.writeText(JSON.stringify(session, null, 2));
  }

  function abortActiveScan() {
    activeScanAbortRef.current?.abort();
    activeScanAbortRef.current = null;
    activeScanIdentityRef.current = null;
    requestInFlightRef.current = false;
    setIsScanningFace(false);
  }

  function resetMetrics() {
    requestSamplesRef.current = [];
    inferenceSamplesRef.current = [];
    maxConcurrentRequestsRef.current = 0;
    setRenderMetrics({
      requestsPerSecond: 0,
      maxConcurrentRequests: 0,
      avgInferenceMs: 0,
      p95InferenceMs: 0,
      acceptedFaceMs: 0,
    });
  }

  function updateMetrics(acceptedFaceMs?: number) {
    const requestRates = requestSamplesRef.current;
    const inferences = [...inferenceSamplesRef.current].sort((a, b) => a - b);
    const avgInferenceMs = inferences.length === 0 ? 0 : inferences.reduce((sum, value) => sum + value, 0) / inferences.length;
    const p95InferenceMs = inferences.length === 0 ? 0 : inferences[Math.min(inferences.length - 1, Math.floor(inferences.length * 0.95))];
    const requestsPerSecond = requestRates.length === 0 ? 0 : requestRates.reduce((sum, value) => sum + value, 0) / requestRates.length;
    setRenderMetrics((current) => ({
      requestsPerSecond,
      maxConcurrentRequests: maxConcurrentRequestsRef.current,
      avgInferenceMs,
      p95InferenceMs,
      acceptedFaceMs: acceptedFaceMs ?? current.acceptedFaceMs,
    }));
  }

  async function captureSnapshot(): Promise<Blob> {
    const video = camera.videoRef.current;
    if (!video || video.videoWidth === 0 || video.videoHeight === 0) {
      throw new Error('Camera preview is not ready.');
    }

    const sourceWidth = video.videoWidth;
    const sourceHeight = video.videoHeight;
    const width = Math.min(SNAPSHOT_MAX_WIDTH, sourceWidth);
    const height = Math.round((sourceHeight / sourceWidth) * width);

    const canvas = captureCanvasRef.current ?? document.createElement('canvas');
    captureCanvasRef.current = canvas;
    canvas.width = width;
    canvas.height = height;

    const context = canvas.getContext('2d');
    if (!context) {
      throw new Error('Canvas 2D context is not available.');
    }

    context.drawImage(video, 0, 0, width, height);
    const blob = await new Promise<Blob | null>((resolve) => {
      canvas.toBlob(resolve, 'image/jpeg', SNAPSHOT_QUALITY);
    });

    if (!blob) {
      throw new Error('Failed to capture snapshot from camera.');
    }

    return blob;
  }

  const faceSlots = session?.faces ?? [];
  const progressText = session?.requestedFaceLabel ?? 'Face 1 of 6';

  return (
    <section className="scanner-panel scanner-test-panel">
      <div className="scanner-video-shell">
        <video ref={camera.videoRef} muted playsInline />
        <canvas ref={overlayCanvasRef} className="scanner-overlay" />
      </div>

      <div className="scanner-controls">
        <h2>6-Face AI Scanner Test</h2>
        <p>
          Camera: {camera.status} | AI health: {aiHealth?.status ?? 'unknown'} | Model: {aiHealth?.modelVersion ?? 'unknown'}
        </p>
        <p>{statusMessage}</p>
        <p>
          1. Start Camera. 2. Start Scan to create a fresh 6-face session. 3. Hold one full face steady and press Scan / Accept Next Face.
        </p>
        {camera.error ? <p className="error-text">{camera.error}</p> : null}
        {error ? <p className="error-text">{error}</p> : null}

        <div className="button-row">
          <button onClick={camera.start} disabled={camera.status === 'starting' || isScanningFace}>Start Camera</button>
          <button onClick={() => void startScanSession()} disabled={isPreparingSession || isScanningFace}>
            {isPreparingSession ? 'Preparing...' : 'Start Scan Session'}
          </button>
          <button onClick={() => void scanCurrentFace()} disabled={camera.status !== 'ready' || isScanningFace || isPreparingSession}>
            {isScanningFace ? 'Scanning...' : 'Scan / Accept Next Face'}
          </button>
          <button onClick={() => void retryCurrentFace()} className="secondary" disabled={!session || isScanningFace}>
            Retry Current Face
          </button>
          <button onClick={() => void resetEntireScan()} className="secondary">
            Reset Entire Scan
          </button>
          <button
            onClick={() => {
              abortActiveScan();
              camera.stop();
            }}
            className="secondary"
          >
            Stop Camera
          </button>
          <button onClick={() => void refreshHealth()} className="secondary" disabled={isCheckingHealth || isScanningFace}>
            {isCheckingHealth ? 'Checking AI...' : 'Refresh AI Health'}
          </button>
        </div>

        <div className="scanner-status-grid">
          <div>
            <span>State</span>
            <strong>{scannerState}</strong>
          </div>
          <div>
            <span>Progress</span>
            <strong>{progressText}</strong>
          </div>
          <div>
            <span>Stable observations</span>
            <strong>{observation ? `${observation.stableObservationCount}/${observation.requiredStableObservations}` : '0/3'}</strong>
          </div>
          <div>
            <span>Detected stickers</span>
            <strong>{observation?.detectedStickers ?? 0}</strong>
          </div>
          <div>
            <span>Total / infer</span>
            <strong>{observation ? `${observation.totalMs.toFixed(0)} / ${observation.inferMs.toFixed(0)} ms` : '0 / 0 ms'}</strong>
          </div>
          <div>
            <span>Decode / post</span>
            <strong>{observation ? `${observation.decodeMs.toFixed(0)} / ${observation.postprocessMs.toFixed(0)} ms` : '0 / 0 ms'}</strong>
          </div>
        </div>

        <div className="scanner-status-grid">
          <div>
            <span>Req/s</span>
            <strong>{renderMetrics.requestsPerSecond.toFixed(2)}</strong>
          </div>
          <div>
            <span>Max concurrent</span>
            <strong>{renderMetrics.maxConcurrentRequests}</strong>
          </div>
          <div>
            <span>Avg / p95 infer</span>
            <strong>{`${renderMetrics.avgInferenceMs.toFixed(0)} / ${renderMetrics.p95InferenceMs.toFixed(0)} ms`}</strong>
          </div>
          <div>
            <span>Accepted face time</span>
            <strong>{renderMetrics.acceptedFaceMs ? `${(renderMetrics.acceptedFaceMs / 1000).toFixed(2)} s` : '-'}</strong>
          </div>
        </div>

        <div className="scanner-face-slots">
          {Array.from({ length: 6 }).map((_, index) => (
            <FaceSlot key={index} index={index} face={faceSlots[index]} active={session?.requestedFaceIndex === index + 1} />
          ))}
        </div>

        {session ? (
          <div className="ai-result-card">
            <div className="ai-result-header">
              <strong>{session.status}</strong>
              <span>{new Date(session.startedAt).toLocaleString()}</span>
            </div>
            <div className="button-row compact">
              <button onClick={() => void copyResultJson()} className="secondary">
                Copy JSON
              </button>
            </div>
            <pre>{JSON.stringify(session, null, 2)}</pre>
          </div>
        ) : null}
      </div>
    </section>
  );
}

function FaceSlot({ index, face, active }: { index: number; face?: AiRubikScannerFace; active: boolean }) {
  return (
    <article className={`scanner-face-slot ${active ? 'active' : ''}`}>
      <header>
        <strong>Face {index + 1}</strong>
        <span>{face?.centerColor ?? 'pending'}</span>
      </header>
      <div className="cube-face-grid">
        {Array.from({ length: 9 }).map((_, cellIndex) => {
          const color = face?.grid3x3?.[Math.floor(cellIndex / 3)]?.[cellIndex % 3] ?? 'unknown';
          return <span key={cellIndex} style={{ background: COLOR_STYLE[color] ?? COLOR_STYLE.unknown }} />;
        })}
      </div>
    </article>
  );
}

function delay(ms: number) {
  return new Promise((resolve) => window.setTimeout(resolve, ms));
}

function applyAcceptedObservation(
  current: AiRubikScannerSessionResponse | null,
  observation: AiRubikScannerPreviewResponse,
  scanGeneration: number,
): AiRubikScannerSessionResponse | null {
  if (
    !current ||
    !observation.grid3x3 ||
    !observation.centerColor ||
    observation.scanSessionId !== current.sessionId ||
    observation.targetFaceIndex !== current.requestedFaceIndex
  ) {
    return current;
  }

  const face = {
    centerColor: observation.centerColor,
    grid3x3: observation.grid3x3,
    stickers: observation.stickers,
    overallConfidence: observation.confidence,
    validFrames: observation.requiredStableObservations,
    capturedAt: new Date().toISOString(),
  };

  const nextFaces = [...current.faces];
  nextFaces[observation.targetFaceIndex - 1] = face;
  const faces = nextFaces.slice(0, 6);
  const rawStickerState = faces.flatMap((savedFace) => savedFace.grid3x3.flat());
  const capturedFaceCount = faces.length;
  const completed = capturedFaceCount >= 6;

  return {
    ...current,
    scanGeneration,
    faces,
    capturedFaceCount,
    rawStickerCount: rawStickerState.length,
    rawStickerState,
    lastFaceScan: face,
    lastScanStatus: 'ACCEPTED',
    lastScanReason: null,
    scannerState: 'ACCEPTED',
    requestedFaceIndex: Math.min(capturedFaceCount + 1, 6),
    requestedFaceLabel: `Face ${Math.min(capturedFaceCount + 1, 6)} of 6`,
    status: completed ? 'COMPLETED' : current.status,
    message: completed ? 'Six-face scan completed.' : 'Face accepted. Rotate to a different center color.',
    completedAt: completed ? new Date().toISOString() : current.completedAt,
  };
}

function createRequestId() {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID();
  }

  return `req-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

function isObservationCurrent(
  observation: AiRubikScannerPreviewResponse,
  expected: { scanSessionId: string; scanGeneration: number; targetFaceIndex: number },
) {
  return observation.scanSessionId === expected.scanSessionId
    && observation.scanGeneration === expected.scanGeneration
    && observation.targetFaceIndex === expected.targetFaceIndex;
}
