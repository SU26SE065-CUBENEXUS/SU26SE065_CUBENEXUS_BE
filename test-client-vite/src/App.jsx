import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { RubikScannerPanel } from './features/rubik-scanner';
import { OnlineArenaScannerPanel } from './features/online-arena-scanner/OnlineArenaScannerPanel';
import { useMatchRecording } from './features/online-arena-recording/useMatchRecording';

const DEFAULT_BACKEND_URL = 'http://localhost:5212';
const DEFAULT_HUB_URL = 'http://localhost:5212/hubs/online-arena';
const SIGNALR_EVENTS = [
  'MatchmakingQueued',
  'MatchmakingFound',
  'MatchmakingCancelled',
  'MatchJoined',
  'CameraReadyUpdated',
  'WebRtcConnectionUpdated',
  'VideoRecordingStarted',
  'TimerConnected',
  'TimerDisconnected',
  'ReadyStateUpdated',
  'MatchReady',
  'AiCheckStarted',
  'AiCheckCompleted',
  'AiCheckFailed',
  'ScrambleRevealed',
  'ScrambleCheckUpdated',
  'FinishCheckUpdated',
  'ResultSubmitted',
  'VideoEvidenceUploaded',
  'MatchNeedsReview',
  'MatchCompleted',
  'MatchCancelled',
  'FraudReportCreated',
  'FraudReportResolved',
  'WebRtcOfferReceived',
  'WebRtcAnswerReceived',
  'IceCandidateReceived',
];

function App() {
  const [backendUrl, setBackendUrl] = useState(DEFAULT_BACKEND_URL);
  const [hubUrl, setHubUrl] = useState(DEFAULT_HUB_URL);
  const [token, setToken] = useState('');
  const [matchId, setMatchId] = useState('');
  const [myUserId, setMyUserId] = useState('');
  const [targetUserId, setTargetUserId] = useState('');
  const [connectionStatus, setConnectionStatus] = useState('disconnected');
  const [peerStatus, setPeerStatus] = useState({
    signalingState: 'idle',
    iceConnectionState: 'idle',
    connectionState: 'idle',
    iceGatheringState: 'idle',
  });
  const [logs, setLogs] = useState([]);

  const hubConnectionRef = useRef(null);
  const peerConnectionRef = useRef(null);
  const localVideoRef = useRef(null);
  const remoteVideoRef = useRef(null);
  const localStreamRef = useRef(null);
  const latestOfferRef = useRef(null);
  const logViewportRef = useRef(null);
  const matchTimeLimitMsRef = useRef(5 * 60 * 1000);
  const recordingMarkedRef = useRef(false);

  const recording = useMatchRecording({
    backendUrl,
    token,
    matchId,
    localVideoRef,
    remoteVideoRef,
    onLog: appendLog,
    onFinalizeTracks: () => stopCamera(),
  });

  useEffect(() => {
    appendLog('info', 'PageReady', 'Online Arena WebRTC test page initialized.');
    return () => {
      if (hubConnectionRef.current) {
        hubConnectionRef.current.stop().catch(() => undefined);
        hubConnectionRef.current = null;
      }
      teardownPeer({ stopLocalStream: true });
    };
  }, []);

  useEffect(() => {
    recordingMarkedRef.current = false;
  }, [matchId, token]);

  useEffect(() => {
    if (logViewportRef.current) {
      logViewportRef.current.scrollTop = logViewportRef.current.scrollHeight;
    }
  }, [logs]);

  function appendLog(level, eventName, payload) {
    setLogs((current) => [
      ...current,
      {
        id: crypto.randomUUID(),
        time: new Date().toLocaleTimeString(),
        level,
        eventName,
        payload,
      },
    ]);
  }

  function setPeerStatusFromPeer(peer) {
    setPeerStatus({
      signalingState: peer?.signalingState ?? 'idle',
      iceConnectionState: peer?.iceConnectionState ?? 'idle',
      connectionState: peer?.connectionState ?? 'idle',
      iceGatheringState: peer?.iceGatheringState ?? 'idle',
    });
  }

  function isConnectionReady() {
    return hubConnectionRef.current && hubConnectionRef.current.state === signalR.HubConnectionState.Connected;
  }

  function ensureRequiredIds() {
    if (!matchId || !myUserId || !targetUserId) {
      appendLog('warn', 'Validation', 'matchId, myUserId, and targetUserId are required.');
      return false;
    }
    return true;
  }

  async function connectHub() {
    if (!token.trim()) {
      appendLog('warn', 'Validation', 'JWT token is required before connecting.');
      return;
    }

    await disconnectHub();
    setConnectionStatus('connecting');
    appendLog('info', 'SignalRConnectStart', { hubUrl });

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => token.trim(),
      })
      .withAutomaticReconnect()
      .build();

    SIGNALR_EVENTS.forEach((eventName) => {
      connection.on(eventName, async (payload) => {
        appendLog('event', eventName, payload);

        if (payload?.timeLimitMs) {
          matchTimeLimitMsRef.current = payload.timeLimitMs;
        }

        if (eventName === 'ScrambleRevealed' && String(payload?.matchId ?? '').toLowerCase() === matchId.trim().toLowerCase()) {
          await recording.startRecording({ timeLimitMs: payload?.timeLimitMs ?? matchTimeLimitMsRef.current });
        } else if (
          (eventName === 'MatchCompleted' || eventName === 'MatchCancelled')
          && String(payload?.matchId ?? '').toLowerCase() === matchId.trim().toLowerCase()
        ) {
          await recording.stopRecording(eventName.toLowerCase());
        }

        if (eventName === 'WebRtcOfferReceived') {
          await handleIncomingOffer(payload);
        } else if (eventName === 'WebRtcAnswerReceived') {
          await handleIncomingAnswer(payload);
        } else if (eventName === 'IceCandidateReceived') {
          await handleIncomingIceCandidate(payload);
        }
      });
    });

    connection.onreconnecting((error) => {
      setConnectionStatus('reconnecting');
      appendLog('warn', 'SignalRReconnecting', error?.message ?? 'Attempting reconnect.');
    });

    connection.onreconnected(async () => {
      setConnectionStatus('connected');
      appendLog('success', 'SignalRReconnected', 'Connection restored.');
      if (matchId) {
        try {
          await connection.invoke('JoinMatchRoom', matchId);
          appendLog('success', 'JoinMatchRoom', { matchId, rejoined: true });
        } catch (error) {
          appendLog('error', 'JoinMatchRoomFailed', error?.message ?? String(error));
        }
      }
    });

    connection.onclose((error) => {
      setConnectionStatus('disconnected');
      appendLog('warn', 'SignalRClosed', error?.message ?? 'Connection closed.');
    });

    try {
      await connection.start();
      hubConnectionRef.current = connection;
      setConnectionStatus('connected');
      appendLog('success', 'SignalRConnected', { connectionId: connection.connectionId });
    } catch (error) {
      setConnectionStatus('disconnected');
      appendLog('error', 'SignalRConnectFailed', error?.message ?? String(error));
    }
  }

  async function disconnectHub() {
    if (!hubConnectionRef.current) {
      setConnectionStatus('disconnected');
      return;
    }

    try {
      await hubConnectionRef.current.stop();
      appendLog('info', 'SignalRDisconnected', 'Hub connection stopped.');
    } catch (error) {
      appendLog('error', 'SignalRDisconnectFailed', error?.message ?? String(error));
    } finally {
      hubConnectionRef.current = null;
      setConnectionStatus('disconnected');
    }
  }

  async function joinMatchRoom() {
    if (!isConnectionReady() || !ensureRequiredIds()) {
      return;
    }

    try {
      await hubConnectionRef.current.invoke('JoinMatchRoom', matchId.trim());
      appendLog('success', 'JoinMatchRoom', { matchId });
    } catch (error) {
      appendLog('error', 'JoinMatchRoomFailed', error?.message ?? String(error));
    }
  }

  async function leaveMatchRoom() {
    if (!isConnectionReady() || !matchId.trim()) {
      return;
    }

    try {
      await hubConnectionRef.current.invoke('LeaveMatchRoom', matchId.trim());
      appendLog('info', 'LeaveMatchRoom', { matchId });
    } catch (error) {
      appendLog('error', 'LeaveMatchRoomFailed', error?.message ?? String(error));
    }
  }

  async function ensureLocalStream() {
    if (localStreamRef.current) {
      return localStreamRef.current;
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: true,
        audio: false,
      });

      localStreamRef.current = stream;
      if (localVideoRef.current) {
        localVideoRef.current.srcObject = stream;
        localVideoRef.current.muted = true;
        localVideoRef.current.playsInline = true;
        void localVideoRef.current.play().catch(() => undefined);
      }
      appendLog('success', 'CameraStarted', 'Local camera stream acquired.');
      return stream;
    } catch (error) {
      appendLog(
        'error',
        'CameraStartFailed',
        error?.message ?? 'Browser blocked camera or no webcam is available. Signaling can still be tested through logs.'
      );
      return null;
    }
  }

  function ensurePeerConnection() {
    if (peerConnectionRef.current) {
      return peerConnectionRef.current;
    }

    const peer = new RTCPeerConnection({
      iceServers: [{ urls: 'stun:stun.l.google.com:19302' }],
    });

    peer.onicecandidate = async (event) => {
      if (!event.candidate) {
        return;
      }

      appendLog('info', 'LocalIceCandidate', event.candidate);
      if (!isConnectionReady() || !ensureRequiredIds()) {
        appendLog('warn', 'IceCandidateSkipped', 'Hub is not connected or IDs are missing.');
        return;
      }

      try {
        await hubConnectionRef.current.invoke(
          'SendIceCandidate',
          matchId.trim(),
          targetUserId.trim(),
          JSON.stringify(event.candidate)
        );
      } catch (error) {
        appendLog('error', 'SendIceCandidateFailed', error?.message ?? String(error));
      }
    };

    peer.ontrack = (event) => {
      const [stream] = event.streams;
      if (remoteVideoRef.current) {
        remoteVideoRef.current.srcObject = stream ?? null;
        remoteVideoRef.current.muted = true;
        remoteVideoRef.current.playsInline = true;
        void remoteVideoRef.current.play().catch(() => undefined);
      }
      appendLog('success', 'RemoteTrack', 'Remote media track attached.');
    };

    peer.onsignalingstatechange = () => setPeerStatusFromPeer(peer);
    peer.oniceconnectionstatechange = () => setPeerStatusFromPeer(peer);
    peer.onconnectionstatechange = () => setPeerStatusFromPeer(peer);
    peer.onicegatheringstatechange = () => setPeerStatusFromPeer(peer);

    if (localStreamRef.current) {
      localStreamRef.current.getTracks().forEach((track) => {
        peer.addTrack(track, localStreamRef.current);
      });
    }

    peerConnectionRef.current = peer;
    setPeerStatusFromPeer(peer);
    appendLog('info', 'PeerCreated', 'RTCPeerConnection ready.');
    return peer;
  }

  async function startCamera() {
    const stream = await ensureLocalStream();
    if (!stream) {
      return;
    }

    const peer = ensurePeerConnection();
    const currentSenders = peer.getSenders().map((sender) => sender.track?.id);
    stream.getTracks().forEach((track) => {
      if (!currentSenders.includes(track.id)) {
        peer.addTrack(track, stream);
      }
    });
    setPeerStatusFromPeer(peer);

    if (matchId.trim() && token.trim() && !recordingMarkedRef.current) {
      try {
        const mime = window.MediaRecorder?.isTypeSupported?.('video/webm;codecs=vp9,opus')
          ? 'video/webm'
          : 'video/webm';
        await recording.markRecordingStarted({
          startedAt: new Date().toISOString(),
          mimeType: mime,
        });
        recordingMarkedRef.current = true;
      } catch (error) {
        appendLog('error', 'RecordingStartedMarkFailed', error?.message ?? String(error));
      }
    }
  }

  function stopCamera() {
    teardownPeer({ stopLocalStream: true });
    appendLog('info', 'CameraStopped', 'Local media tracks stopped.');
  }

  function teardownPeer({ stopLocalStream }) {
    if (peerConnectionRef.current) {
      peerConnectionRef.current.onicecandidate = null;
      peerConnectionRef.current.ontrack = null;
      peerConnectionRef.current.close();
      peerConnectionRef.current = null;
    }

    if (stopLocalStream && localStreamRef.current) {
      localStreamRef.current.getTracks().forEach((track) => track.stop());
      localStreamRef.current = null;
    }

    latestOfferRef.current = null;

    if (localVideoRef.current) {
      localVideoRef.current.srcObject = localStreamRef.current;
    }
    if (remoteVideoRef.current) {
      remoteVideoRef.current.srcObject = null;
    }

    setPeerStatus({
      signalingState: 'idle',
      iceConnectionState: 'idle',
      connectionState: 'idle',
      iceGatheringState: 'idle',
    });
  }

  async function createOffer() {
    if (!isConnectionReady() || !ensureRequiredIds()) {
      return;
    }

    await startCamera();
    const peer = ensurePeerConnection();

    try {
      const offer = await peer.createOffer();
      await peer.setLocalDescription(offer);
      appendLog('success', 'LocalOfferCreated', peer.localDescription);
      await hubConnectionRef.current.invoke(
        'SendWebRtcOffer',
        matchId.trim(),
        targetUserId.trim(),
        JSON.stringify(peer.localDescription)
      );
      appendLog('success', 'SendWebRtcOffer', { matchId, targetUserId });
    } catch (error) {
      appendLog('error', 'CreateOfferFailed', error?.message ?? String(error));
    }
  }

  async function createAnswerFromLatestOffer() {
    if (!latestOfferRef.current) {
      appendLog('warn', 'CreateAnswerSkipped', 'No latest offer is available.');
      return;
    }

    await createAnswerFromOfferPayload(latestOfferRef.current);
  }

  async function createAnswerFromOfferPayload(payload) {
    const fromUserId = String(payload?.fromUserId ?? '');
    if (!fromUserId) {
      appendLog('error', 'CreateAnswerFailed', 'Incoming offer payload is missing fromUserId.');
      return;
    }

    const stream = await ensureLocalStream();
    if (!stream) {
      appendLog('warn', 'AnswerNeedsCamera', 'Start Camera and retry Create Answer if auto camera failed.');
    }

    const peer = ensurePeerConnection();

    try {
      const remoteOffer = JSON.parse(payload.offer);
      await peer.setRemoteDescription(remoteOffer);
      const answer = await peer.createAnswer();
      await peer.setLocalDescription(answer);
      appendLog('success', 'LocalAnswerCreated', peer.localDescription);

      if (!isConnectionReady()) {
        appendLog('error', 'SendWebRtcAnswerFailed', 'Hub is not connected.');
        return;
      }

      await hubConnectionRef.current.invoke(
        'SendWebRtcAnswer',
        String(payload.matchId),
        fromUserId,
        JSON.stringify(peer.localDescription)
      );
      appendLog('success', 'SendWebRtcAnswer', { matchId: payload.matchId, targetUserId: fromUserId });
    } catch (error) {
      appendLog('error', 'CreateAnswerFailed', error?.message ?? String(error));
    }
  }

  async function handleIncomingOffer(payload) {
    latestOfferRef.current = payload;
    if (String(payload?.fromUserId ?? '').toLowerCase() !== targetUserId.trim().toLowerCase()) {
      appendLog('warn', 'OfferSourceMismatch', payload);
    }

    await createAnswerFromOfferPayload(payload);
  }

  async function handleIncomingAnswer(payload) {
    const peer = ensurePeerConnection();
    try {
      await peer.setRemoteDescription(JSON.parse(payload.answer));
      appendLog('success', 'RemoteAnswerApplied', payload);
    } catch (error) {
      appendLog('error', 'ApplyAnswerFailed', error?.message ?? String(error));
    }
  }

  async function handleIncomingIceCandidate(payload) {
    const peer = ensurePeerConnection();
    try {
      await peer.addIceCandidate(JSON.parse(payload.candidate));
      appendLog('success', 'RemoteIceCandidateApplied', payload);
    } catch (error) {
      appendLog('error', 'ApplyIceCandidateFailed', error?.message ?? String(error));
    }
  }

  function clearLogs() {
    setLogs([]);
  }

  async function copyLogs() {
    try {
      const value = logs
        .map((log) => `[${log.time}] ${log.level.toUpperCase()} ${log.eventName}\n${formatPayload(log.payload)}`)
        .join('\n\n');
      await navigator.clipboard.writeText(value);
      appendLog('success', 'CopyLogs', 'Logs copied to clipboard.');
    } catch (error) {
      appendLog('error', 'CopyLogsFailed', error?.message ?? String(error));
    }
  }

  function fillTokenFromLocalStorage() {
    const candidateKeys = ['token', 'accessToken', 'jwt', 'authToken'];
    const foundKey = candidateKeys.find((key) => localStorage.getItem(key));
    if (!foundKey) {
      appendLog('warn', 'LocalStorageTokenMissing', 'No token found in localStorage keys: token, accessToken, jwt, authToken.');
      return;
    }

    const foundToken = localStorage.getItem(foundKey) ?? '';
    setToken(foundToken);
    appendLog('success', 'LocalStorageTokenLoaded', { key: foundKey });
  }

  function formatPayload(payload) {
    if (payload == null) {
      return '';
    }
    return typeof payload === 'string' ? payload : JSON.stringify(payload, null, 2);
  }

  return (
    <div className="app-shell">
      <section className="hero">
        <div>
          <p className="eyebrow">Online Arena Dev Console</p>
          <h1>SignalR + WebRTC signaling test page</h1>
          <p className="subcopy">
            Use two browsers or two tabs with different JWT tokens to validate hub connection, match room events,
            and offer/answer/ice forwarding.
          </p>
        </div>
        <div className={`connection-pill ${connectionStatus}`}>
          <span className="dot" />
          <span>{connectionStatus}</span>
        </div>
      </section>

      <section className="grid-layout">
        <div className="panel">
          <h2>Hub Setup</h2>
          <label>Backend URL</label>
          <input value={backendUrl} onChange={(event) => setBackendUrl(event.target.value)} />
          <label>Hub URL</label>
          <input value={hubUrl} onChange={(event) => setHubUrl(event.target.value)} />
          <label>JWT Token</label>
          <textarea rows="5" value={token} onChange={(event) => setToken(event.target.value)} />
          <label>Match ID</label>
          <input value={matchId} onChange={(event) => setMatchId(event.target.value)} />
          <label>My User ID</label>
          <input value={myUserId} onChange={(event) => setMyUserId(event.target.value)} />
          <label>Target User ID</label>
          <input value={targetUserId} onChange={(event) => setTargetUserId(event.target.value)} />

          <div className="button-row">
            <button onClick={connectHub}>Connect Hub</button>
            <button onClick={disconnectHub} className="secondary">Disconnect Hub</button>
            <button onClick={fillTokenFromLocalStorage} className="secondary">Fill token from localStorage</button>
          </div>

          <div className="button-row">
            <button onClick={joinMatchRoom}>Join Match Room</button>
            <button onClick={leaveMatchRoom} className="secondary">Leave Match Room</button>
          </div>
        </div>

        <div className="panel">
          <h2>WebRTC Controls</h2>
          <div className="button-row">
            <button onClick={startCamera}>Start Camera</button>
            <button onClick={stopCamera} className="secondary">Stop Camera</button>
          </div>
          <div className="button-row">
            <button onClick={createOffer}>Create Offer</button>
            <button onClick={createAnswerFromLatestOffer}>Create Answer / Retry Answer</button>
            <button onClick={() => teardownPeer({ stopLocalStream: true })} className="secondary">Hang Up / Close Peer</button>
          </div>

          <div className="status-grid">
            <div><span>signalingState</span><strong>{peerStatus.signalingState}</strong></div>
            <div><span>iceConnectionState</span><strong>{peerStatus.iceConnectionState}</strong></div>
            <div><span>connectionState</span><strong>{peerStatus.connectionState}</strong></div>
            <div><span>iceGatheringState</span><strong>{peerStatus.iceGatheringState}</strong></div>
          </div>

          <div className="video-grid">
            <div className="video-card">
              <div className="video-label">Local video</div>
              <video ref={localVideoRef} autoPlay muted playsInline />
            </div>
            <div className="video-card">
              <div className="video-label">Remote video</div>
              <video ref={remoteVideoRef} autoPlay playsInline />
            </div>
          </div>
        </div>

        <div className="panel panel-wide">
          <div className="panel-header">
            <h2>Realtime event log</h2>
            <div className="button-row compact">
              <button onClick={clearLogs} className="secondary">Clear Logs</button>
              <button onClick={copyLogs} className="secondary">Copy Logs</button>
            </div>
          </div>
          <div className="log-viewport" ref={logViewportRef}>
            {logs.map((log) => (
              <article key={log.id} className={`log-entry ${log.level}`}>
                <header>
                  <span>{log.time}</span>
                  <strong>{log.eventName}</strong>
                </header>
                {log.payload ? <pre>{formatPayload(log.payload)}</pre> : null}
              </article>
            ))}
          </div>
        </div>

        <div className="panel panel-wide">
          <RubikScannerPanel backendUrl={backendUrl} />
        </div>

        <div className="panel panel-wide">
          <OnlineArenaScannerPanel backendUrl={backendUrl} />
        </div>

        <div className="panel panel-wide">
          <div className="panel-header">
            <h2>Match Recording</h2>
            <div className="button-row compact">
              <button onClick={() => recording.startRecording({ timeLimitMs: matchTimeLimitMsRef.current })}>
                Start Recording
              </button>
              <button onClick={() => recording.stopRecording('manual-stop')} className="secondary">
                Stop + Upload
              </button>
              <button onClick={() => recording.retryUpload()} className="secondary">
                Retry Upload
              </button>
              <button onClick={() => recording.refreshPlayback()} className="secondary">
                Load Playback URLs
              </button>
            </div>
          </div>

          <div className="status-grid">
            <div><span>Status</span><strong>{recording.status}</strong></div>
            <div><span>MIME</span><strong>{recording.mimeType || '-'}</strong></div>
            <div><span>Duration</span><strong>{recording.durationSeconds ? `${recording.durationSeconds.toFixed(2)} s` : '-'}</strong></div>
            <div><span>Upload progress</span><strong>{`${recording.uploadProgress}%`}</strong></div>
          </div>

          <div className="status-grid">
            <div><span>Recorded at</span><strong>{recording.recordedAt ?? '-'}</strong></div>
            <div><span>Marked started</span><strong>{recording.recordingStartedMarkedAt ?? '-'}</strong></div>
            <div><span>Object key</span><strong>{recording.objectKey || '-'}</strong></div>
            <div><span>Auto cap</span><strong>8m match + 2m finish scan</strong></div>
            <div><span>Source</span><strong>Local + Remote composite</strong></div>
          </div>

          {recording.error ? <p className="error-text">{recording.error}</p> : null}
          {recording.recordingStartedMarkedAt ? (
            <p>Recording-started is now marked automatically when you press <code>Start Camera</code> on a loaded match.</p>
          ) : (
            <p>Load a real match, then press <code>Start Camera</code> once so backend preparation is marked before auto recording begins.</p>
          )}
          {recording.status === 'failed' ? (
            <p>Upload failed. Press <code>Retry Upload</code> after checking network, JWT, backend R2 config, and bucket permissions.</p>
          ) : null}
          {recording.status === 'ready' ? (
            <p>Recording uploaded successfully. Use the playback buttons below to verify the signed R2 video.</p>
          ) : null}
          {recording.playback?.recordings?.length ? (
            <div className="button-row">
              {recording.playback.recordings.map((item) => (
                <a key={item.videoEvidenceId} href={item.playbackUrl} target="_blank" rel="noreferrer">
                  <button type="button" className="secondary">
                    View Video {item.playerId.slice(0, 8)}
                  </button>
                </a>
              ))}
            </div>
          ) : null}
          {recording.playback ? <pre>{JSON.stringify(recording.playback, null, 2)}</pre> : null}
        </div>

        <div className="panel panel-wide">
          <h2>Local test checklist</h2>
          <ol className="checklist">
            <li>Run backend at <code>{backendUrl}</code> and frontend with <code>npm run dev</code>.</li>
            <li>Use Swagger to create a match and collect token A/B, userId A/B, matchId, roomToken, qrSessionCode.</li>
            <li>Open Chrome for User A and Edge for User B to avoid mixed localStorage tokens.</li>
            <li>In both browsers, paste token, matchId, myUserId, targetUserId, then click Connect Hub and Join Match Room.</li>
            <li>Use REST endpoints for camera ready, timer connect, ready, start, submit result, and watch the realtime log.</li>
            <li>Use the standalone 6-Face AI Scanner Test panel to verify centralized AI scanning without JWT, match state, or production arena checks.</li>
            <li>Use the OnlineArena AI Scanner Test panel as a JWT-free sandbox first, then port that stable scanner flow back into the real online match flow.</li>
            <li>When the match emits ScrambleRevealed, the recording hook auto-starts and records a single composite video of local + remote players.</li>
            <li>When MatchCompleted or MatchCancelled arrives, recording auto-stops, uploads to R2 with a presigned URL, then calls complete.</li>
            <li>If webcam is available on both sides, click Start Camera before Create Offer.</li>
            <li>If second browser cannot access the camera, continue testing offer, answer, ice, and AI check logs separately.</li>
          </ol>
        </div>
      </section>
    </div>
  );
}

export default App;
