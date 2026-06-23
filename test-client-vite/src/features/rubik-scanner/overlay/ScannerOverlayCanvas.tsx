import { useEffect, useRef } from 'react';
import type { FrameDetectionResult } from '../types';

type Props = {
  video: HTMLVideoElement | null;
  frame: FrameDetectionResult | null;
};

export function ScannerOverlayCanvas({ video, frame }: Props) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
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
    frame?.orderedBoxes?.forEach((box, index) => {
      context.strokeStyle = '#facc15';
      context.lineWidth = 2;
      context.strokeRect(box.x1, box.y1, box.x2 - box.x1, box.y2 - box.y1);
      context.fillStyle = '#111827';
      context.fillText(String(index + 1), box.x1 + 4, box.y1 + 14);
    });
  }, [video, frame]);

  return <canvas ref={canvasRef} className="scanner-overlay" />;
}
