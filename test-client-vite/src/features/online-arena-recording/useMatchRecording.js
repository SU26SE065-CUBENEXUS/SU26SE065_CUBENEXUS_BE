/**
 * @deprecated Client-side HTML5 canvas compositing is deprecated.
 * System architecture has evolved to Single Local Stream Capture + Asynchronous Cloud Split-Screen Stitching.
 * Production implementation resides in web/SU26SE065_CUBENEXUS_FE/features/online-arena/contexts/MatchLocalRecordingContext.tsx
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  completeMatchRecordingUpload,
  createMatchRecordingUploadUrl,
  getMatchRecordingPlaybackUrls,
  markMatchRecordingStarted,
  uploadRecordingBlob,
} from './api';

const DEFAULT_TIME_LIMIT_MS = 5 * 60 * 1000;
const MAX_TOTAL_RECORDING_MS = 10 * 60 * 1000;

export function useMatchRecording({
  backendUrl,
  token,
  matchId,
  localVideoRef,
  remoteVideoRef,
  onLog,
  onFinalizeTracks,
}) {
  const [status, setStatus] = useState('idle');
  const [error, setError] = useState(null);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [mimeType, setMimeType] = useState('');
  const [durationSeconds, setDurationSeconds] = useState(0);
  const [objectKey, setObjectKey] = useState('');
  const [recordedAt, setRecordedAt] = useState(null);
  const [playback, setPlayback] = useState(null);
  const [recordingStartedMarkedAt, setRecordingStartedMarkedAt] = useState(null);

  const canvasRef = useRef(null);
  const captureStreamRef = useRef(null);
  const mediaRecorderRef = useRef(null);
  const drawFrameRef = useRef(null);
  const stopTimerRef = useRef(null);
  const chunksRef = useRef([]);
  const startedAtMsRef = useRef(0);
  const pendingBlobRef = useRef(null);
  const pendingUploadMetaRef = useRef(null);
  const stoppingRef = useRef(false);

  const log = useCallback((level, eventName, payload) => {
    onLog?.(level, eventName, payload);
  }, [onLog]);

  const cleanupCapturePipeline = useCallback(() => {
    if (drawFrameRef.current) {
      cancelAnimationFrame(drawFrameRef.current);
      drawFrameRef.current = null;
    }
    if (stopTimerRef.current) {
      clearTimeout(stopTimerRef.current);
      stopTimerRef.current = null;
    }
    if (captureStreamRef.current) {
      captureStreamRef.current.getTracks().forEach((track) => track.stop());
      captureStreamRef.current = null;
    }
    mediaRecorderRef.current = null;
    stoppingRef.current = false;
  }, []);

  const resetSessionState = useCallback(() => {
    setUploadProgress(0);
    setError(null);
    setPlayback(null);
    setObjectKey('');
  }, []);

  const uploadBlobToR2 = useCallback(async (blob, metadata) => {
    if (!backendUrl.trim() || !token.trim() || !matchId.trim()) {
      throw new Error('backendUrl, token, and matchId are required before uploading recording.');
    }

    setStatus('preparing-upload');
    const uploadTicket = await createMatchRecordingUploadUrl({
      backendUrl,
      token,
      matchId: matchId.trim(),
      contentType: metadata.contentType,
      fileExtension: metadata.extension,
      durationSeconds: metadata.durationSeconds,
      recordedAt: metadata.recordedAt,
    });

    setObjectKey(uploadTicket.objectKey);
    setStatus('uploading');
    setUploadProgress(0);

    await uploadRecordingBlob({
      uploadUrl: uploadTicket.uploadUrl,
      contentType: uploadTicket.contentType,
      blob,
      onProgress: (progress) => setUploadProgress(progress),
    });

    setStatus('finalizing');
    const completed = await completeMatchRecordingUpload({
      backendUrl,
      token,
      matchId: matchId.trim(),
      objectKey: uploadTicket.objectKey,
      durationSeconds: metadata.durationSeconds,
    });

    setStatus('ready');
    setUploadProgress(100);
    setObjectKey(completed.objectKey);
    log('success', 'RecordingUploadCompleted', completed);
    const playbackUrls = await getMatchRecordingPlaybackUrls({
      backendUrl,
      token,
      matchId: matchId.trim(),
    });
    setPlayback(playbackUrls);
    pendingBlobRef.current = null;
    pendingUploadMetaRef.current = null;
  }, [backendUrl, token, matchId, log]);

  const stopRecording = useCallback(async (reason = 'manual-stop') => {
    if (!mediaRecorderRef.current || stoppingRef.current) {
      return;
    }

    stoppingRef.current = true;
    setStatus('stopping');
    log('info', 'RecordingStopRequested', { reason });

    const recorder = mediaRecorderRef.current;
    const finalizedBlobPromise = new Promise((resolve, reject) => {
      recorder.onstop = async () => {
        const durationMs = Math.max(0, Date.now() - startedAtMsRef.current);
        const blob = new Blob(chunksRef.current, { type: mimeType || recorder.mimeType || 'video/webm' });
        chunksRef.current = [];
        cleanupCapturePipeline();
        onFinalizeTracks?.();

        const uploadMeta = {
          contentType: blob.type || recorder.mimeType || 'video/webm',
          extension: extensionFromMimeType(blob.type || recorder.mimeType || 'video/webm'),
          durationSeconds: Number((durationMs / 1000).toFixed(3)),
          recordedAt: recordedAt ?? new Date(startedAtMsRef.current).toISOString(),
        };

        setDurationSeconds(uploadMeta.durationSeconds);
        pendingBlobRef.current = blob;
        pendingUploadMetaRef.current = uploadMeta;

        try {
          await uploadBlobToR2(blob, uploadMeta);
          resolve();
        } catch (uploadError) {
          setStatus('failed');
          setError(uploadError instanceof Error ? uploadError.message : String(uploadError));
          log('error', 'RecordingUploadFailed', uploadError instanceof Error ? uploadError.message : String(uploadError));
          reject(uploadError);
        }
      };
    });

    recorder.stop();
    await finalizedBlobPromise.catch(() => undefined);
  }, [cleanupCapturePipeline, log, mimeType, onFinalizeTracks, recordedAt, uploadBlobToR2]);

  const startRecording = useCallback(async ({ timeLimitMs = DEFAULT_TIME_LIMIT_MS } = {}) => {
    if (status === 'recording' || status === 'starting') {
      return;
    }
    if (!window.MediaRecorder) {
      setStatus('failed');
      setError('MediaRecorder is not supported in this browser.');
      return;
    }
    if (!localVideoRef.current && !remoteVideoRef.current) {
      setStatus('failed');
      setError('No video elements are available for recording.');
      return;
    }

    resetSessionState();
    setStatus('starting');

    try {
      const localVideo = localVideoRef.current;
      const remoteVideo = remoteVideoRef.current;
      await ensureVideoPlayback(localVideo);
      await ensureVideoPlayback(remoteVideo);

      const readyCount = await waitForRenderableVideoCount([localVideo, remoteVideo], 3000);
      if (readyCount === 0) {
        throw new Error('No playable local/remote video frames are ready. Press Start Camera, complete WebRTC connection, then try Start Recording again.');
      }

      const canvas = canvasRef.current ?? document.createElement('canvas');
      canvasRef.current = canvas;
      canvas.width = 1280;
      canvas.height = 720;
      const context = canvas.getContext('2d');
      if (!context) {
        throw new Error('Canvas 2D context is not available for recording.');
      }

      const selectedMimeType = resolveRecorderMimeType();
      const stream = canvas.captureStream(24);
      captureStreamRef.current = stream;

      const recorder = new MediaRecorder(stream, { mimeType: selectedMimeType });
      mediaRecorderRef.current = recorder;
      chunksRef.current = [];
      setMimeType(selectedMimeType);
      setRecordedAt(new Date().toISOString());
      startedAtMsRef.current = Date.now();

      recorder.ondataavailable = (event) => {
        if (event.data && event.data.size > 0) {
          chunksRef.current.push(event.data);
        }
      };

      recorder.onerror = (event) => {
        const message = event.error?.message || 'MediaRecorder error.';
        setStatus('failed');
        setError(message);
        log('error', 'RecordingRuntimeError', message);
      };

      const draw = () => {
        drawCompositeFrame(context, canvas, localVideoRef.current, remoteVideoRef.current);
        drawFrameRef.current = requestAnimationFrame(draw);
      };
      draw();

      recorder.start(1000);
      setStatus('recording');
      log('success', 'RecordingStarted', { mimeType: selectedMimeType, matchId, timeLimitMs });

      const effectiveLimitMs = Math.min(MAX_TOTAL_RECORDING_MS, Math.max(timeLimitMs, DEFAULT_TIME_LIMIT_MS) + 120000);
      stopTimerRef.current = setTimeout(() => {
        void stopRecording('max-duration-reached');
      }, effectiveLimitMs);
    } catch (startError) {
      cleanupCapturePipeline();
      setStatus('failed');
      setError(startError instanceof Error ? startError.message : String(startError));
      log('error', 'RecordingStartFailed', startError instanceof Error ? startError.message : String(startError));
    }
  }, [cleanupCapturePipeline, localVideoRef, log, matchId, remoteVideoRef, resetSessionState, status, stopRecording]);

  const retryUpload = useCallback(async () => {
    if (!pendingBlobRef.current || !pendingUploadMetaRef.current) {
      return;
    }
    setError(null);
    await uploadBlobToR2(pendingBlobRef.current, pendingUploadMetaRef.current).catch(() => undefined);
  }, [uploadBlobToR2]);

  const refreshPlayback = useCallback(async () => {
    if (!backendUrl.trim() || !token.trim() || !matchId.trim()) {
      return;
    }
    try {
      const playbackUrls = await getMatchRecordingPlaybackUrls({
        backendUrl,
        token,
        matchId: matchId.trim(),
      });
      setPlayback(playbackUrls);
      log('info', 'RecordingPlaybackLoaded', playbackUrls);
    } catch (playbackError) {
      setError(playbackError instanceof Error ? playbackError.message : String(playbackError));
    }
  }, [backendUrl, token, matchId, log]);

  const markRecordingStarted = useCallback(async ({ startedAt, mimeType }) => {
    if (!backendUrl.trim() || !token.trim() || !matchId.trim()) {
      throw new Error('backendUrl, token, and matchId are required before marking recording started.');
    }

    const response = await markMatchRecordingStarted({
      backendUrl,
      token,
      matchId: matchId.trim(),
      recordingStartedAt: startedAt,
      mimeType,
    });
    setRecordingStartedMarkedAt(startedAt);
    log('success', 'RecordingStartedMarked', response);
    return response;
  }, [backendUrl, token, matchId, log]);

  useEffect(() => {
    return () => {
      cleanupCapturePipeline();
    };
  }, [cleanupCapturePipeline]);

  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'hidden' && mediaRecorderRef.current && !stoppingRef.current) {
        void stopRecording('page-hidden');
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => document.removeEventListener('visibilitychange', handleVisibilityChange);
  }, [stopRecording]);

  return {
    status,
    error,
    uploadProgress,
    mimeType,
    durationSeconds,
    objectKey,
    recordedAt,
    playback,
    recordingStartedMarkedAt,
    isRecording: status === 'recording',
    startRecording,
    stopRecording,
    markRecordingStarted,
    retryUpload,
    refreshPlayback,
  };
}

function resolveRecorderMimeType() {
  const candidates = [
    'video/webm;codecs=vp9,opus',
    'video/webm;codecs=vp8,opus',
    'video/webm',
    'video/mp4',
  ];

  return candidates.find((candidate) => MediaRecorder.isTypeSupported(candidate)) || '';
}

function extensionFromMimeType(mimeType) {
  const normalized = mimeType.split(';', 1)[0].trim().toLowerCase();
  return normalized === 'video/mp4' ? 'mp4' : 'webm';
}

async function ensureVideoPlayback(video) {
  if (!video || !video.srcObject) {
    return;
  }

  video.autoplay = true;
  video.playsInline = true;

  try {
    await video.play();
  } catch {
    // Ignore autoplay failures here; readiness check below will decide whether recording can start.
  }
}

async function waitForRenderableVideoCount(videos, timeoutMs) {
  const startedAt = Date.now();

  while (Date.now() - startedAt < timeoutMs) {
    const readyCount = videos.filter(isRenderableVideo).length;
    if (readyCount > 0) {
      return readyCount;
    }

    await new Promise((resolve) => setTimeout(resolve, 120));
  }

  return videos.filter(isRenderableVideo).length;
}

function isRenderableVideo(video) {
  return Boolean(
    video
    && video.srcObject
    && video.readyState >= 2
    && video.videoWidth > 0
    && video.videoHeight > 0
  );
}

function drawCompositeFrame(context, canvas, localVideo, remoteVideo) {
  context.fillStyle = '#0f172a';
  context.fillRect(0, 0, canvas.width, canvas.height);

  const gap = 20;
  const slotWidth = Math.floor((canvas.width - gap * 3) / 2);
  const slotHeight = canvas.height - gap * 2;

  drawVideoSlot(context, {
    x: gap,
    y: gap,
    width: slotWidth,
    height: slotHeight,
    video: localVideo,
    title: 'Player A',
  });

  drawVideoSlot(context, {
    x: gap * 2 + slotWidth,
    y: gap,
    width: slotWidth,
    height: slotHeight,
    video: remoteVideo,
    title: 'Player B',
  });

  context.fillStyle = 'rgba(15, 23, 42, 0.76)';
  context.fillRect(20, canvas.height - 42, 220, 26);
  context.fillStyle = '#f8fafc';
  context.font = '14px Segoe UI';
  context.fillText(new Date().toLocaleTimeString(), 32, canvas.height - 24);
}

function drawVideoSlot(context, { x, y, width, height, video, title }) {
  context.fillStyle = '#111827';
  context.fillRect(x, y, width, height);
  context.strokeStyle = '#14b8a6';
  context.lineWidth = 3;
  context.strokeRect(x, y, width, height);

  if (isRenderableVideo(video)) {
    const videoRatio = video.videoWidth / video.videoHeight;
    const slotRatio = width / height;
    let drawWidth = width;
    let drawHeight = height;
    let offsetX = x;
    let offsetY = y;

    if (videoRatio > slotRatio) {
      drawHeight = width / videoRatio;
      offsetY = y + (height - drawHeight) / 2;
    } else {
      drawWidth = height * videoRatio;
      offsetX = x + (width - drawWidth) / 2;
    }

    context.drawImage(video, offsetX, offsetY, drawWidth, drawHeight);
  } else {
    context.fillStyle = '#cbd5e1';
    context.font = '24px Segoe UI';
    context.fillText('Waiting for video...', x + 28, y + height / 2);
  }

  context.fillStyle = 'rgba(15, 23, 42, 0.78)';
  context.fillRect(x + 12, y + 12, 96, 24);
  context.fillStyle = '#f8fafc';
  context.font = '14px Segoe UI';
  context.fillText(title, x + 22, y + 29);
}
