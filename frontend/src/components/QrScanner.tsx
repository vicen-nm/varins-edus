import { useEffect, useRef } from 'react';
import jsQR from 'jsqr';

// Frames are shrunk before reading them: a QR on a screen stays readable and the phone does less work.
const MAX_FRAME_WIDTH = 640;

type Props = {
  // Reading frames or not. The camera stays open either way, so scanning again is instant.
  active: boolean;
  onDetected: (text: string) => void;
  onError: (message: string) => void;
};

export function QrScanner({ active, onDetected, onError }: Props) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);

  // The latest callbacks, so a new function on every render does not restart the camera or the reading.
  const detectedRef = useRef(onDetected);
  const errorRef = useRef(onError);

  useEffect(() => {
    detectedRef.current = onDetected;
    errorRef.current = onError;
  });

  // The camera opens once and always closes when the component goes away: it must never stay on.
  useEffect(() => {
    let cancelled = false;
    let stream: MediaStream | null = null;

    async function open() {
      if (!navigator.mediaDevices?.getUserMedia) {
        errorRef.current('Este navegador no permite usar la cámara.');
        return;
      }

      try {
        const opened = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: 'environment' } },
          audio: false,
        });

        if (cancelled) {
          opened.getTracks().forEach((track) => track.stop());
          return;
        }

        stream = opened;

        const video = videoRef.current;

        if (video) {
          video.srcObject = opened;
          void video.play().catch(() => {
            // Autoplay can fail; the person can tap the video.
          });
        }
      } catch {
        if (!cancelled) {
          errorRef.current('No se pudo abrir la cámara. Revisa el permiso del navegador.');
        }
      }
    }

    void open();

    return () => {
      cancelled = true;
      stream?.getTracks().forEach((track) => track.stop());
    };
  }, []);

  // While active: look for a QR in every frame. The first one found is reported once.
  useEffect(() => {
    if (!active) {
      return;
    }

    const video = videoRef.current;
    const canvas = canvasRef.current;
    const context = canvas?.getContext('2d', { willReadFrequently: true });

    if (!video || !canvas || !context) {
      return;
    }

    let stopped = false;
    let frame = 0;

    function tick() {
      if (stopped) {
        return;
      }

      if (video!.readyState === video!.HAVE_ENOUGH_DATA && video!.videoWidth > 0) {
        const scale = Math.min(1, MAX_FRAME_WIDTH / video!.videoWidth);
        canvas!.width = Math.round(video!.videoWidth * scale);
        canvas!.height = Math.round(video!.videoHeight * scale);
        context!.drawImage(video!, 0, 0, canvas!.width, canvas!.height);

        const image = context!.getImageData(0, 0, canvas!.width, canvas!.height);
        const code = jsQR(image.data, image.width, image.height, { inversionAttempts: 'dontInvert' });

        if (code?.data) {
          stopped = true;
          detectedRef.current(code.data);
          return;
        }
      }

      frame = requestAnimationFrame(tick);
    }

    frame = requestAnimationFrame(tick);

    return () => {
      stopped = true;
      cancelAnimationFrame(frame);
    };
  }, [active]);

  return (
    <>
      <video ref={videoRef} className="scan-video" playsInline muted />
      <canvas ref={canvasRef} hidden />
    </>
  );
}