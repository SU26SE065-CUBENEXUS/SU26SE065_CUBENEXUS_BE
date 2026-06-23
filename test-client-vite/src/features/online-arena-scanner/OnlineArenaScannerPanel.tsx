import { useEffect, useRef, useState, type MutableRefObject } from 'react';
import { runScannerBurst } from '../rubik-scanner/scanBurstControl';
import { useCameraStream } from '../rubik-scanner/camera/useCameraStream';
import {
  getOnlineArenaScannerSession,
  getOnlineMatchDetail,
  mockOnlineMatchFinishPass,
  observeOnlineArenaScannerFrame,
  reconcileOnlineMatchStatus,
  resetOnlineArenaScannerSession,
  retryOnlineArenaScannerFace,
  startOnlineArenaScannerSession,
  type OnlineArenaMatchDetail,
  type OnlineArenaScannerSessionResponse,
} from './api';

type Props = {
  backendUrl: string;
  token: string;
  matchId: string;
};

const CAPTURE_INTERVAL_MS = 220;
const MAX_SCAN_BURST_MS = 8500;
const SNAPSHOT_MAX_WIDTH = 800;
const SNAPSHOT_QUALITY = 0.88;

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
  POSITION_FACE: 'Đưa đúng mặt đang được yêu cầu vào giữa khung hình.',
  SCANNING: 'Giữ đủ 9 sticker trong khung và giữ yên khoảng 1-2 giây.',
  STABLE: 'Đã thấy mặt cube. Giữ nguyên thêm một chút để đủ 3 lần ổn định.',
  ACCEPTED: 'Mặt đã được chấp nhận. Xoay sang mặt tiếp theo đúng màu tâm được yêu cầu.',
  DUPLICATE_FACE: 'Bạn đang quét lại mặt đã chấp nhận trước đó. Hãy đổi sang mặt khác.',
  RETRY: 'Nhận diện chưa ổn định. Giữ lại đúng mặt này và bấm Retry Current Face rồi scan lại.',
  AI_BUSY: 'AI service đang bận. Chờ một chút rồi scan lại.',
  AI_UNAVAILABLE: 'AI service chưa sẵn sàng. Kiểm tra AI backend rồi thử lại.',
  CAMERA_ERROR: 'Không đọc được ảnh camera. Hãy dừng camera và bật lại.',
};

export function OnlineArenaScannerPanel({ backendUrl, token, matchId }: Props) {
  const camera = useCameraStream();
  const [validationType, setValidationType] = useState<'scramble' | 'finish'>('scramble');
  const [session, setSession] = useState<OnlineArenaScannerSessionResponse | null>(null);
  const [matchDetail, setMatchDetail] = useState<OnlineArenaMatchDetail | null>(null);
  const [statusMessage, setStatusMessage] = useState('Load a match, then start a scanner session.');
  const [error, setError] = useState<string | null>(null);
  const [isLoadingMatch, setIsLoadingMatch] = useState(false);
  const [isPreparingSession, setIsPreparingSession] = useState(false);
  const [isScanningFace, setIsScanningFace] = useState(false);
  const [isReconciling, setIsReconciling] = useState(false);
  const [isMockingFinishPass, setIsMockingFinishPass] = useState(false);
  const currentUserId = decodeJwtUserId(token);
  const currentPlayerSlot = resolvePlayerSlot(matchDetail, currentUserId);

  const captureCanvasRef = useRef<HTMLCanvasElement | null>(null);
  const overlayCanvasRef = useRef<HTMLCanvasElement | null>(null);
  const activeScanAbortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    return () => {
      activeScanAbortRef.current?.abort();
    };
  }, []);

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
    const observedCenter = session?.observedCenterColor || '';
    const remainingCenters = getRemainingCenterColors(session);
    const primaryLabel = observedCenter
      ? `Observed center: ${observedCenter}`
      : 'Observed center: waiting';
    const secondaryLabel = remainingCenters.length > 0
      ? `Remaining: ${remainingCenters.join(', ')}`
      : 'All 6 centers captured';

    context.clearRect(0, 0, width, height);
    context.strokeStyle = 'rgba(20, 184, 166, 0.72)';
    context.lineWidth = 2;
    context.setLineDash([10, 10]);
    context.strokeRect(inset, inset, width - inset * 2, height - inset * 2);
    context.setLineDash([]);

    context.fillStyle = 'rgba(15, 23, 42, 0.78)';
    context.fillRect(12, 12, Math.min(520, width - 24), 40);
    context.fillStyle = '#f8fafc';
    context.font = '12px Segoe UI';
    context.fillText(primaryLabel, 20, 27);
    context.fillText(secondaryLabel, 20, 43);
  }, [camera.videoRef, session?.observedCenterColor, session?.faces, session?.requestedFaceCode]);

  useEffect(() => {
    setSession(null);
    setMatchDetail(null);
    setError(null);
    setStatusMessage('Load a match, then start a scanner session.');
  }, [matchId, validationType, token]);

  async function loadMatchDetail() {
    if (!matchId.trim()) {
      setError('Match ID is required.');
      return;
    }

    setIsLoadingMatch(true);
    try {
      const detail = await getOnlineMatchDetail({ backendUrl, token, matchId: matchId.trim() });
      setMatchDetail(detail);
      setError(null);
      setStatusMessage('Match loaded. Start the camera, then start a scanner session.');
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setIsLoadingMatch(false);
    }
  }

  async function loadExistingSession() {
    if (!matchId.trim()) {
      setError('Match ID is required.');
      return;
    }

    setIsPreparingSession(true);
    try {
      const current = await getOnlineArenaScannerSession({
        backendUrl,
        token,
        matchId: matchId.trim(),
        validationType,
      });
      setSession(current);
      setError(null);
      setStatusMessage(current.message || 'Scanner session loaded.');
      await loadMatchDetail();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setIsPreparingSession(false);
    }
  }

  async function startSession() {
    if (!matchId.trim()) {
      setError('Match ID is required.');
      return;
    }

    activeScanAbortRef.current?.abort();
    setIsPreparingSession(true);
    try {
      const started = await startOnlineArenaScannerSession({
        backendUrl,
        token,
        matchId: matchId.trim(),
        validationType,
      });
      setSession(started);
      setError(null);
      setStatusMessage(started.message || 'Scanner session started.');
      await loadMatchDetail();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setIsPreparingSession(false);
    }
  }

  async function scanCurrentFace() {
    if (camera.status !== 'ready') {
      setError('Start the camera before scanning.');
      return;
    }

    if (!session) {
      setError('Start the scanner session first.');
      return;
    }

    if (session.scanStatus === 'COMPLETED') {
      setStatusMessage('This scanner session is already completed. Reset or start a new one.');
      return;
    }

    let currentSession = session;
    if (currentSession.scannerState === 'RETRY' || currentSession.scannerState === 'DUPLICATE_FACE') {
      const recovered = await retryOnlineArenaScannerFace({
        backendUrl,
        token,
        matchId: matchId.trim(),
        validationType,
      });
      currentSession = recovered;
      setSession(recovered);
      setStatusMessage(recovered.message || UI_MESSAGE[recovered.scannerState] || 'Retry current face.');
    }

    const abortController = new AbortController();
    activeScanAbortRef.current?.abort();
    activeScanAbortRef.current = abortController;
    setIsScanningFace(true);
    setError(null);
    setStatusMessage(`${currentSession.requestedFaceLabel || 'Scan next face.'} Hold the cube still.`);

    try {
      let lastResponse: OnlineArenaScannerSessionResponse | null = null;
      const runSingleBurst = async () => runScannerBurst({
        capture: async () => captureSnapshot(camera.videoRef.current, captureCanvasRef),
        observe: async (snapshot: Blob) => {
          const response = await observeOnlineArenaScannerFrame({
            backendUrl,
            token,
            matchId: matchId.trim(),
            validationType,
            scanSessionId: currentSession.scanSessionId,
            scanGeneration: currentSession.scanGeneration,
            requestId: createRequestId(),
            targetFaceIndex: currentSession.requestedFaceIndex,
            snapshot,
            signal: abortController.signal,
          });
          currentSession = response;
          lastResponse = response;
          setSession(response);
          setStatusMessage(response.reason || response.message || UI_MESSAGE[response.scannerState] || response.scannerState);
          return { scannerState: response.scannerState, response };
        },
        shouldStop: (observation) => {
          const state = observation?.scannerState;
          return state === 'ACCEPTED'
            || state === 'DUPLICATE_FACE'
            || state === 'AI_UNAVAILABLE'
            || state === 'CAMERA_ERROR';
        },
        delay,
        shouldAbort: () => abortController.signal.aborted,
        maxBurstMs: MAX_SCAN_BURST_MS,
        sampleIntervalMs: CAPTURE_INTERVAL_MS,
        now: () => performance.now(),
      });

      let result = await runSingleBurst();
      if (result.reason === 'timeout' && !abortController.signal.aborted) {
        setStatusMessage('AI is still collecting stable frames. Keep the cube steady, the system is trying one more pass automatically...');
        result = await runSingleBurst();
      }

      if (result.reason === 'timeout' && !abortController.signal.aborted) {
        setStatusMessage('AI still did not get enough stable frames. Hold the cube a bit steadier, reduce glare, then press Scan again.');
      }

      if (lastResponse?.scannerState === 'ACCEPTED' || lastResponse?.scanStatus === 'COMPLETED' || !!lastResponse?.validation) {
        await loadMatchDetail();
        if (validationType === 'finish' && (lastResponse?.scanStatus === 'COMPLETED' || !!lastResponse?.validation)) {
          await reconcileStatus();
        }
      }
    } catch (err) {
      if (!abortController.signal.aborted) {
        setError(err instanceof Error ? err.message : String(err));
      }
    } finally {
      if (activeScanAbortRef.current === abortController) {
        activeScanAbortRef.current = null;
      }
      setIsScanningFace(false);
    }
  }

  async function retryFace() {
    if (!session) {
      setError('Start or load a scanner session first.');
      return;
    }

    activeScanAbortRef.current?.abort();
    try {
      const updated = await retryOnlineArenaScannerFace({
        backendUrl,
        token,
        matchId: matchId.trim(),
        validationType,
      });
      setSession(updated);
      setError(null);
      setStatusMessage(updated.message || 'Retry current face.');
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  }

  async function resetSession() {
    if (!session) {
      setError('Start or load a scanner session first.');
      return;
    }

    activeScanAbortRef.current?.abort();
    try {
      const updated = await resetOnlineArenaScannerSession({
        backendUrl,
        token,
        matchId: matchId.trim(),
        validationType,
      });
      setSession(updated);
      setError(null);
      setStatusMessage(updated.message || 'Scanner session reset.');
      await loadMatchDetail();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  }

  async function reconcileStatus() {
    if (!matchId.trim()) {
      setError('Match ID is required.');
      return;
    }

    setIsReconciling(true);
    try {
      const detail = await reconcileOnlineMatchStatus({
        backendUrl,
        token,
        matchId: matchId.trim(),
      });
      setMatchDetail(detail);
      setError(null);
      setStatusMessage('Match status reconciled from backend.');
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setIsReconciling(false);
    }
  }

  async function mockFinishPass() {
    if (!matchId.trim()) {
      setError('Match ID is required.');
      return;
    }

    setIsMockingFinishPass(true);
    try {
      const detail = await mockOnlineMatchFinishPass({
        backendUrl,
        token,
        matchId: matchId.trim(),
      });
      setMatchDetail(detail);
      setError(null);
      setStatusMessage('Mock finish pass applied for this player.');
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setIsMockingFinishPass(false);
    }
  }

  const faces = session?.faces ?? [];
  const validation = session?.validation;
  const nextStepMessage = buildNextStepMessageV2(validationType, session, matchDetail);

  return (
    <section className="scanner-panel scanner-test-panel">
      <div className="scanner-video-shell">
        <video ref={camera.videoRef} muted playsInline />
        <canvas ref={overlayCanvasRef} className="scanner-overlay" />
      </div>

      <div className="scanner-controls">
        <h2>OnlineArena AI Scanner Test</h2>
        <p>Test backend scanner APIs with the same camera-style flow as the standalone scanner, but bound to a real online match.</p>
        <p>{statusMessage}</p>
        <p>
          1. Load Match. 2. Start Camera. 3. Start {validationType} session. 4. Hold any unscanned face steady. 5. Press Scan / Accept Next Face.
        </p>
        <p>
          OnlineArena mode now accepts faces in any order. Backend maps each accepted face to
          {' '}<code>U/R/F/D/L/B</code> from the observed center color before validation.
        </p>
        <p>
          In scramble mode, the face does not need to be a solid single color. The stickers can be mixed by the scramble.
          You only need one complete face with 9 visible stickers and a center color that has not been accepted yet.
        </p>
        <p>
          Start Scan Session only opens a backend AI session bound to this match and player. It does not require SignalR/WebRTC hub
          connection to detect stickers.
        </p>
        {nextStepMessage ? <p><strong>Next step:</strong> {nextStepMessage}</p> : null}
        {camera.error ? <p className="error-text">{camera.error}</p> : null}
        {error ? <p className="error-text">{error}</p> : null}

        <div className="button-row">
          <button
            type="button"
            className={validationType === 'scramble' ? '' : 'secondary'}
            onClick={() => setValidationType('scramble')}
          >
            Scramble Mode
          </button>
          <button
            type="button"
            className={validationType === 'finish' ? '' : 'secondary'}
            onClick={() => setValidationType('finish')}
          >
            Finish Mode
          </button>
        </div>

        <div className="button-row">
          <button onClick={() => void loadMatchDetail()} disabled={isLoadingMatch}>
            {isLoadingMatch ? 'Loading Match...' : 'Load Match'}
          </button>
          <button onClick={camera.start} disabled={camera.status === 'starting' || isScanningFace}>
            Start Camera
          </button>
          <button onClick={() => void startSession()} disabled={isPreparingSession || isScanningFace}>
            {isPreparingSession ? 'Preparing...' : 'Start Scan Session'}
          </button>
          <button onClick={() => void scanCurrentFace()} disabled={camera.status !== 'ready' || !session || isScanningFace}>
            {isScanningFace ? 'Scanning...' : 'Scan / Accept Next Face'}
          </button>
          <button onClick={() => void retryFace()} className="secondary" disabled={!session || isScanningFace}>
            Retry Current Face
          </button>
          <button onClick={() => void resetSession()} className="secondary" disabled={!session}>
            Reset Session
          </button>
          <button onClick={() => void loadExistingSession()} className="secondary" disabled={isPreparingSession || isScanningFace}>
            Load Existing Session
          </button>
          <button onClick={() => void reconcileStatus()} className="secondary" disabled={isReconciling || isScanningFace}>
            {isReconciling ? 'Reconciling...' : 'Reconcile Match Status'}
          </button>
          <button onClick={() => void mockFinishPass()} className="secondary" disabled={isMockingFinishPass || isScanningFace}>
            {isMockingFinishPass ? 'Mocking...' : 'Mock Finish Passed'}
          </button>
          <button onClick={camera.stop} className="secondary">
            Stop Camera
          </button>
        </div>

        <div className="scanner-status-grid">
          <div>
            <span>Auth user</span>
            <strong>{currentUserId ? currentUserId.slice(0, 8) : '-'}</strong>
          </div>
          <div>
            <span>Acting as</span>
            <strong>{currentPlayerSlot}</strong>
          </div>
          <div>
            <span>Mode</span>
            <strong>{validationType}</strong>
          </div>
          <div>
            <span>Match</span>
            <strong>{matchDetail?.statusCode ?? '-'}</strong>
          </div>
          <div>
            <span>Scan status</span>
            <strong>{session?.scanStatus ?? '-'}</strong>
          </div>
          <div>
            <span>Scanner state</span>
            <strong>{session?.scannerState ?? '-'}</strong>
          </div>
          <div>
            <span>Requested face</span>
            <strong>{session ? `${session.requestedFaceLabel || '-'} / ${session.requestedCenterColor || '-'}` : '-'}</strong>
          </div>
          <div>
            <span>Captured faces</span>
            <strong>{session?.capturedFaceCount ?? 0} / 6</strong>
          </div>
          <div>
            <span>Stable observations</span>
            <strong>{session ? `${session.stableObservationCount}/${session.requiredStableObservations}` : '0/3'}</strong>
          </div>
          <div>
            <span>Detected stickers</span>
            <strong>{session?.detectedStickers ?? 0}</strong>
          </div>
          <div>
            <span>Total / infer</span>
            <strong>{session ? `${session.totalMs.toFixed(0)} / ${session.inferMs.toFixed(0)} ms` : '0 / 0 ms'}</strong>
          </div>
          <div>
            <span>Scan session</span>
            <strong>{session?.scanSessionId ? session.scanSessionId.slice(0, 8) : '-'}</strong>
          </div>
        </div>

        {matchDetail ? (
          <div className="ai-result-card">
            <div className="ai-result-header">
              <strong>Online Match Detail</strong>
              <span>{matchDetail.id}</span>
            </div>
            <pre>{JSON.stringify(matchDetail, null, 2)}</pre>
          </div>
        ) : null}

        {validation ? (
          <div className="ai-result-card">
            <div className="ai-result-header">
              <strong>Validation Result</strong>
              <span>{validation.status}</span>
            </div>
            <pre>{JSON.stringify(validation, null, 2)}</pre>
          </div>
        ) : null}

        <div className="scanner-face-slots">
          {Array.from({ length: 6 }).map((_, index) => (
            <OnlineArenaFaceSlot key={index} index={index} face={faces[index]} active={index === getNextPendingFaceSlotIndex(faces)} />
          ))}
        </div>
      </div>
    </section>
  );
}

function OnlineArenaFaceSlot({
  index,
  face,
  active,
}: {
  index: number;
  face?: OnlineArenaScannerSessionResponse['faces'][number];
  active: boolean;
}) {
  return (
    <article className={`scanner-face-slot ${active ? 'active' : ''}`}>
      <header>
        <strong>{face?.faceCode ?? `Face ${index + 1}`}</strong>
        <span>{face?.observedCenterColor ?? face?.expectedCenterColor ?? 'pending'}</span>
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

async function captureSnapshot(
  video: HTMLVideoElement | null,
  canvasRef: MutableRefObject<HTMLCanvasElement | null>,
): Promise<Blob> {
  if (!video || video.videoWidth === 0 || video.videoHeight === 0) {
    throw new Error('Camera preview is not ready.');
  }

  const sourceWidth = video.videoWidth;
  const sourceHeight = video.videoHeight;
  const width = Math.min(SNAPSHOT_MAX_WIDTH, sourceWidth);
  const height = Math.round((sourceHeight / sourceWidth) * width);

  const canvas = canvasRef.current ?? document.createElement('canvas');
  canvasRef.current = canvas;
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

function delay(ms: number) {
  return new Promise((resolve) => window.setTimeout(resolve, ms));
}

function createRequestId() {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID();
  }

  return `req-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

function buildNextStepMessageV2(
  validationType: 'scramble' | 'finish',
  session: OnlineArenaScannerSessionResponse | null,
  matchDetail: OnlineArenaMatchDetail | null,
) {
  if (!session) {
    return null;
  }

  if (session.scanStatus !== 'COMPLETED') {
    return 'Continue scanning any face that has not been accepted yet. Keep all 9 stickers visible and avoid repeating a center color.';
  }

  if (validationType === 'scramble') {
    if (session.validation?.status === 'PASS') {
      if (matchDetail?.statusCode === 'READY') {
        return 'Scramble is valid. When both players are VERIFIED_READY, call Start Match to move into the live solve.';
      }

      return 'Scramble is valid. Wait for the opponent to finish scanning so the match can move to READY.';
    }

    return 'Scramble did not match the assigned expected state. Reset the session, fix the cube to the assigned scramble, then scan 6 faces again.';
  }

  if (session.validation?.status === 'PASS') {
    return 'Finish state is valid. If both players already submitted results and both finish validations pass, backend will complete the match.';
  }

  return 'Finish state is not valid yet. Check that the cube is solved, then reset the session and scan all 6 faces again.';
}

function getNextPendingFaceSlotIndex(faces: OnlineArenaScannerSessionResponse['faces']) {
  for (let index = 0; index < 6; index += 1) {
    if (!faces[index]) {
      return index;
    }
  }

  return -1;
}

function getRemainingCenterColors(session: OnlineArenaScannerSessionResponse | null) {
  const allCenters = ['white', 'red', 'green', 'yellow', 'orange', 'blue'];
  const captured = new Set((session?.faces ?? []).map((face) => (face.observedCenterColor || face.expectedCenterColor || '').toLowerCase()));
  return allCenters.filter((color) => !captured.has(color));
}

function decodeJwtUserId(token: string) {
  try {
    if (!token.trim()) {
      return '';
    }

    const [, payload] = token.split('.');
    if (!payload) {
      return '';
    }

    const normalized = payload.replace(/-/g, '+').replace(/_/g, '/');
    const json = atob(normalized.padEnd(normalized.length + ((4 - normalized.length % 4) % 4), '='));
    const parsed = JSON.parse(json) as Record<string, string | undefined>;
    return parsed.id || parsed.userId || parsed.sub || '';
  } catch {
    return '';
  }
}

function resolvePlayerSlot(matchDetail: OnlineArenaMatchDetail | null, userId: string) {
  if (!matchDetail || !userId) {
    return '-';
  }

  if (matchDetail.player1Id.toLowerCase() === userId.toLowerCase()) {
    return 'PLAYER_1';
  }

  if (matchDetail.player2Id.toLowerCase() === userId.toLowerCase()) {
    return 'PLAYER_2';
  }

  return 'NOT_IN_MATCH';
}

function buildNextStepMessage(
  validationType: 'scramble' | 'finish',
  session: OnlineArenaScannerSessionResponse | null,
  matchDetail: OnlineArenaMatchDetail | null,
) {
  if (!session) {
    return null;
  }

  if (session.scanStatus !== 'COMPLETED') {
    return `Tiếp tục quét đúng mặt ${session.requestedFaceCode || session.requestedFaceIndex} với tâm màu ${session.requestedCenterColor || 'đang yêu cầu'}.`;
  }

  if (validationType === 'scramble') {
    if (session.validation?.status === 'PASS') {
      if (matchDetail?.statusCode === 'READY') {
        return 'Scramble đã hợp lệ. Khi cả hai người đều VERIFIED_READY, gọi API Start Match để chuyển sang thi đấu.';
      }
      return 'Scramble đã hợp lệ. Giờ hãy chờ đối thủ scan xong để match chuyển sang READY.';
    }

    return 'Scramble chưa khớp expected state. Hãy Reset Session, sửa cube theo scramble được cấp, rồi quét lại đủ 6 mặt.';
  }

  if (session.validation?.status === 'PASS') {
    return 'Finish state đã hợp lệ. Nếu cả hai người đã submit result và finish validation đều PASS, backend sẽ hoàn tất match.';
  }

  return 'Finish state chưa hợp lệ. Hãy kiểm tra cube đã solved chưa, rồi Reset Session và quét lại 6 mặt.';
}
