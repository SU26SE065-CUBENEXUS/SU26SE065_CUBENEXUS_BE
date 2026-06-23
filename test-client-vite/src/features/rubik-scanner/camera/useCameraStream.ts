import { useCallback, useEffect, useRef, useState } from 'react';

export function useCameraStream() {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const [deviceLabel, setDeviceLabel] = useState('');
  const [status, setStatus] = useState<'idle' | 'starting' | 'ready' | 'failed'>('idle');
  const [error, setError] = useState<string | null>(null);

  const start = useCallback(async () => {
    setStatus('starting');
    setError(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { width: { ideal: 640 }, height: { ideal: 480 }, facingMode: 'environment' },
        audio: false,
      });
      streamRef.current = stream;
      setDeviceLabel(stream.getVideoTracks()[0]?.label ?? '');
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        await videoRef.current.play();
      }
      setStatus('ready');
    } catch (err) {
      setStatus('failed');
      setError(err instanceof Error ? err.message : String(err));
    }
  }, []);

  const stop = useCallback(() => {
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    if (videoRef.current) {
      videoRef.current.srcObject = null;
    }
    setStatus('idle');
  }, []);

  useEffect(() => stop, [stop]);

  return { videoRef, status, error, deviceLabel, start, stop };
}
