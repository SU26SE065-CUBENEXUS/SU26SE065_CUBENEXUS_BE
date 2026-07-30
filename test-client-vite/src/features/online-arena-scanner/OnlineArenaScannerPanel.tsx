import { useEffect, useRef, useState } from 'react';
import {
  fetchScannerTestHealth,
  observeScannerTestFrame,
  resetScannerTestSession,
  retryScannerTestFace,
  startScannerTestSession,
} from '../rubik-scanner/api/onlineScannerApi';
import { useCameraStream } from '../rubik-scanner/camera/useCameraStream';
import { runScannerBurst } from '../rubik-scanner/scanBurstControl';
import type {
  AiRubikHealthResponse,
  AiRubikScannerFace,
  AiRubikScannerPreviewResponse,
  AiRubikScannerSessionResponse,
} from '../rubik-scanner/types';

type Props = {
  backendUrl: string;
};

const CAPTURE_INTERVAL_MS = 220;
const MAX_SCAN_BURST_MS = 6500;
const SNAPSHOT_MAX_WIDTH = 800;
const SNAPSHOT_QUALITY = 0.82;

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
  POSITION_FACE: 'Đưa trọn 1 mặt vào giữa khung scan.',
  SCANNING: 'AI đang đọc mặt hiện tại. Giữ yên thêm một chút.',
  STABLE: 'Đã thấy mặt cube rõ. Giữ nguyên để đủ độ ổn định.',
  ACCEPTED: 'Mặt đã được nhận. Xoay sang mặt có tâm màu khác.',
  DUPLICATE_FACE: 'Mặt này đã được nhận trước đó. Hãy đổi sang mặt khác.',
  RETRY: 'Detection unstable. Điều chỉnh cube rồi bấm scan lại.',
  AI_BUSY: 'AI đang bận. Chờ một chút rồi thử lại.',
  AI_UNAVAILABLE: 'AI chưa sẵn sàng. Kiểm tra service rồi thử lại.',
  CAMERA_ERROR: 'Camera chưa sẵn sàng. Hãy start lại camera.',
};

export function OnlineArenaScannerPanel({ backendUrl }: Props) {
  const camera = useCameraStream();
  const [scanMode, setScanMode] = useState<'scramble' | 'finish'>('scramble');
  const [aiHealth, setAiHealth] = useState<AiRubikHealthResponse | null>(null);
  const [session, setSession] = useState<AiRubikScannerSessionResponse | null>(null);
  const [observation, setObservation] = useState<AiRubikScannerPreviewResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [scannerState, setScannerState] = useState<AiRubikScannerPreviewResponse['scannerState'] | AiRubikScannerSessionResponse['scannerState']>('POSITION_FACE');
  const [statusMessage, setStatusMessage] = useState('Bấm Start Camera, sau đó Start Scan Session để test AI trực tiếp.');
  const [isCheckingHealth, setIsCheckingHealth] = useState(false);
  const [isPreparingSession, setIsPreparingSession] = useState(false);
  const [isScanningFace, setIsScanningFace] = useState(false);

  const captureCanvasRef = useRef<HTMLCanvasElement | null>(null);
  const overlayCanvasRef = useRef<HTMLCanvasElement | null>(null);
  const activeScanAbortRef = useRef<AbortController | null>(null);
  const activeScanIdentityRef = useRef<{ scanSessionId: string; scanGeneration: number; targetFaceIndex: number } | null>(null);
  const scanGenerationRef = useRef(0);

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

    const width = canvas.width;
    const height = canvas.height;
    const inset = Math.round(Math.min(width, height) * 0.08);

    context.clearRect(0, 0, width, height);
    context.strokeStyle = 'rgba(249, 115, 22, 0.85)';
    context.lineWidth = 2;
    context.setLineDash([10, 8]);
    context.strokeRect(inset, inset, width - inset * 2, height - inset * 2);
    context.setLineDash([]);

    context.fillStyle = 'rgba(15, 23, 42, 0.82)';
    context.fillRect(12, 12, Math.min(420, width - 24), 54);
    context.fillStyle = '#f8fafc';
    context.font = '12px Segoe UI';
    context.fillText(`Observed center: ${(observation?.centerColor ?? '-').toUpperCase()}`, 20, 29);
    context.fillText(`Remaining: ${getRemainingCenterColors(session).map(capitalize).join(', ') || 'Completed'}`, 20, 46);

    observation?.stickers.forEach((sticker, index) => {
      const [x1, y1, x2, y2] = sticker.bbox;
      context.strokeStyle = '#facc15';
      context.lineWidth = 2;
      context.strokeRect(x1, y1, x2 - x1, y2 - y1);
      context.fillStyle = 'rgba(17,24,39,0.82)';
      context.fillRect(x1, Math.max(0, y1 - 20), 108, 18);
      context.fillStyle = '#f8fafc';
      context.fillText(`${index + 1}. ${sticker.color}`, x1 + 4, Math.max(12, y1 - 7));
    });
  }, [camera.videoRef, observation, session]);

  useEffect(() => {
    abortActiveScan();
    setSession(null);
    setObservation(null);
    setError(null);
    setScannerState('POSITION_FACE');
    setStatusMessage(`Đang ở ${scanMode} mode. Start Scan Session để test AI thuần không cần match.`);
  }, [scanMode]);

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
      setStatusMessage('Session đã sẵn sàng. Giữ một mặt ổn định rồi bấm Scan / Accept Next Face.');
      setError(null);
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
    setStatusMessage(`Đang scan ${currentSession.requestedFaceLabel}. Giữ yên cube để AI khóa mặt.`);
    setError(null);

    try {
      const result = await runScannerBurst({
        capture: captureSnapshot,
        observe: async (snapshot) => observeScannerTestFrame({
          backendUrl,
          sessionId: currentSession.sessionId,
          snapshot,
          ...scanIdentity,
          requestId: createRequestId(),
          signal: abortController.signal,
        }),
        onObservation: (nextObservation) => {
          if (!isObservationCurrent(nextObservation, scanIdentity)) {
            return;
          }

          setObservation(nextObservation);
          setScannerState(nextObservation.scannerState);
          setStatusMessage(nextObservation.reason || UI_MESSAGE[nextObservation.scannerState]);

          if (nextObservation.scannerState === 'ACCEPTED') {
            setSession((current) => applyAcceptedObservation(current, nextObservation, scanIdentity.scanGeneration));
          }
        },
        shouldStop: (nextObservation) => (
          nextObservation.scannerState === 'ACCEPTED'
          || nextObservation.scannerState === 'DUPLICATE_FACE'
          || nextObservation.scannerState === 'AI_UNAVAILABLE'
          || nextObservation.scannerState === 'CAMERA_ERROR'
        ),
        shouldAbort: () => abortController.signal.aborted,
        maxBurstMs: MAX_SCAN_BURST_MS,
        sampleIntervalMs: CAPTURE_INTERVAL_MS,
        delay,
        now: () => performance.now(),
      });

      if (result.reason === 'timeout' && !abortController.signal.aborted) {
        setScannerState('RETRY');
        setStatusMessage('AI chưa đủ frame ổn định. Giữ thẳng hơn, bớt chói sáng, rồi bấm scan lại.');
      }
    } catch (err) {
      if (abortController.signal.aborted) {
        return;
      }

      setError(err instanceof Error ? err.message : String(err));
      setScannerState('AI_UNAVAILABLE');
      setStatusMessage(UI_MESSAGE.AI_UNAVAILABLE);
    } finally {
      if (activeScanAbortRef.current === abortController) {
        activeScanAbortRef.current = null;
      }
      if (activeScanIdentityRef.current?.scanGeneration === scanIdentity.scanGeneration) {
        activeScanIdentityRef.current = null;
      }
      setIsScanningFace(false);
    }
  }

  async function retryFace() {
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
      setStatusMessage('Đã xóa trạng thái mặt hiện tại. Canh lại đúng mặt đó rồi scan tiếp.');
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  }

  async function resetSession() {
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
      setStatusMessage('Session đã reset. Bạn có thể scan lại từ đầu ngay bây giờ.');
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  }

  function abortActiveScan() {
    activeScanAbortRef.current?.abort();
    activeScanAbortRef.current = null;
    activeScanIdentityRef.current = null;
    setIsScanningFace(false);
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
      throw new Error('Failed to capture a camera snapshot.');
    }

    return blob;
  }

  const faceSlots = session?.faces ?? [];
  const observedCenterText = observation?.centerColor ? observation.centerColor.toUpperCase() : '-';
  const remainingCenters = getRemainingCenterColors(session).map(capitalize);
  const progressText = `${session?.capturedFaceCount ?? 0} / 6`;
  const stableText = observation ? `${observation.stableObservationCount} / ${observation.requiredStableObservations}` : '0 / 3';

  return (
    <section className="scanner-panel scanner-test-panel">
      <div className="scanner-video-shell">
        <video ref={camera.videoRef} muted playsInline />
        <canvas ref={overlayCanvasRef} className="scanner-overlay" />
      </div>

      <div className="scanner-controls">
        <h2>OnlineArena AI Scanner Test</h2>
        <p>Phiên bản sandbox này bỏ qua match và JWT để bạn test luồng AI hoàn chỉnh trước.</p>
        <p>{statusMessage}</p>
        <p>
          1. Start Camera. 2. Start Scan Session. 3. Giữ một mặt đủ 9 stickers. 4. Bấm Scan / Accept Next Face.
        </p>
        <p>
          Khi flow này đã mượt, mình có thể lấy nguyên logic ổn định này để gắn lại vào online match flow sau.
        </p>
        {camera.error ? <p className="error-text">{camera.error}</p> : null}
        {error ? <p className="error-text">{error}</p> : null}

        <div className="button-row">
          <button
            type="button"
            className={scanMode === 'scramble' ? '' : 'secondary'}
            onClick={() => setScanMode('scramble')}
          >
            Scramble Mode
          </button>
          <button
            type="button"
            className={scanMode === 'finish' ? '' : 'secondary'}
            onClick={() => setScanMode('finish')}
          >
            Finish Mode
          </button>
        </div>

        <div className="button-row">
          <button onClick={camera.start} disabled={camera.status === 'starting' || isScanningFace}>
            Start Camera
          </button>
          <button onClick={() => void startScanSession()} disabled={isPreparingSession || isScanningFace}>
            {isPreparingSession ? 'Preparing...' : 'Start Scan Session'}
          </button>
          <button onClick={() => void scanCurrentFace()} disabled={camera.status !== 'ready' || isScanningFace || isPreparingSession}>
            {isScanningFace ? 'Scanning...' : 'Scan / Accept Next Face'}
          </button>
          <button onClick={() => void retryFace()} className="secondary" disabled={!session || isScanningFace}>
            Retry Current Face
          </button>
          <button onClick={() => void resetSession()} className="secondary" disabled={isScanningFace}>
            Reset Session
          </button>
          <button onClick={camera.stop} className="secondary">
            Stop Camera
          </button>
          <button onClick={() => void refreshHealth()} className="secondary" disabled={isCheckingHealth || isScanningFace}>
            {isCheckingHealth ? 'Checking AI...' : 'Refresh AI Health'}
          </button>
        </div>

        <div className="scanner-status-grid">
          <div>
            <span>Mode</span>
            <strong>{scanMode}</strong>
          </div>
          <div>
            <span>AI health</span>
            <strong>{aiHealth?.status ?? 'unknown'}</strong>
          </div>
          <div>
            <span>Model</span>
            <strong>{aiHealth?.modelVersion ?? '-'}</strong>
          </div>
          <div>
            <span>Progress</span>
            <strong>{progressText} Captured</strong>
          </div>
          <div>
            <span>Stability Check</span>
            <strong>{stableText} Matches</strong>
          </div>
          <div>
            <span>AI Infer Time</span>
            <strong>{observation ? `${observation.inferMs.toFixed(0)} ms` : '-'}</strong>
          </div>
          <div>
            <span>State</span>
            <strong>{scannerState}</strong>
          </div>
          <div>
            <span>Stickers</span>
            <strong>{observation?.detectedStickers ?? 0} / 9</strong>
          </div>
          <div>
            <span>Observed center</span>
            <strong>{observedCenterText}</strong>
          </div>
        </div>

        <div className="ai-result-card">
          <div className="ai-result-header">
            <strong>Remaining Center Colors</strong>
            <span>{remainingCenters.length ? `${remainingCenters.length} left` : 'Completed'}</span>
          </div>
          <p>{remainingCenters.length ? remainingCenters.join(', ') : 'All 6 center colors captured.'}</p>
          <p>
            Chế độ test này không ép mặt đơn sắc. Chỉ cần AI thấy đủ 9 stickers và tâm màu chưa bị trùng là có thể nhận mặt.
          </p>
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
        <strong>{face ? `Face ${index + 1}` : 'Pending'}</strong>
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

function applyAcceptedObservation(
  current: AiRubikScannerSessionResponse | null,
  observation: AiRubikScannerPreviewResponse,
  scanGeneration: number,
): AiRubikScannerSessionResponse | null {
  if (
    !current
    || !observation.grid3x3
    || !observation.centerColor
    || observation.scanSessionId !== current.sessionId
    || observation.targetFaceIndex !== current.requestedFaceIndex
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

function getRemainingCenterColors(session: AiRubikScannerSessionResponse | null) {
  const allCenters = ['white', 'red', 'green', 'yellow', 'orange', 'blue'];
  const captured = new Set((session?.faces ?? []).map((face) => face.centerColor.toLowerCase()));
  return allCenters.filter((color) => !captured.has(color));
}

function capitalize(value: string) {
  return value ? `${value[0].toUpperCase()}${value.slice(1)}` : value;
}
