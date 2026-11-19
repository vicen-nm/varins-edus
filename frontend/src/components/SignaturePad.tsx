import { useEffect, useRef } from 'react';
import type { PointerEvent } from 'react';

type Props = {
  // The signature as a PNG data URL, or null when the pad is empty.
  onChange: (dataUrl: string | null) => void;
};

const WIDTH = 600;
const HEIGHT = 200;

// White background and black ink: the image looks the same on a light or a dark screen.
function paint(canvas: HTMLCanvasElement) {
  const context = canvas.getContext('2d');

  if (!context) {
    return;
  }

  context.fillStyle = '#ffffff';
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.strokeStyle = '#000000';
  context.lineWidth = 3;
  context.lineCap = 'round';
  context.lineJoin = 'round';
}

export function SignaturePad({ onChange }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const drawing = useRef(false);
  const inked = useRef(false);

  useEffect(() => {
    if (canvasRef.current) {
      paint(canvasRef.current);
    }
  }, []);

  // The canvas is drawn at 600x200 and shown at the width of the screen.
  function position(event: PointerEvent<HTMLCanvasElement>) {
    const canvas = event.currentTarget;
    const rect = canvas.getBoundingClientRect();

    return {
      x: (event.clientX - rect.left) * (canvas.width / rect.width),
      y: (event.clientY - rect.top) * (canvas.height / rect.height),
    };
  }

  function handleDown(event: PointerEvent<HTMLCanvasElement>) {
    const canvas = event.currentTarget;
    const context = canvas.getContext('2d');

    if (!context) {
      return;
    }

    // The finger or the mouse keep drawing even when they leave the edge of the canvas.
    canvas.setPointerCapture(event.pointerId);
    drawing.current = true;

    const { x, y } = position(event);

    context.beginPath();
    context.moveTo(x, y);
    context.lineTo(x + 0.01, y);
    context.stroke();
    inked.current = true;
  }

  function handleMove(event: PointerEvent<HTMLCanvasElement>) {
    if (!drawing.current) {
      return;
    }

    const context = event.currentTarget.getContext('2d');

    if (!context) {
      return;
    }

    const { x, y } = position(event);

    context.lineTo(x, y);
    context.stroke();
  }

  function handleUp(event: PointerEvent<HTMLCanvasElement>) {
    if (!drawing.current) {
      return;
    }

    drawing.current = false;

    if (inked.current) {
      onChange(event.currentTarget.toDataURL('image/png'));
    }
  }

  function clear() {
    if (canvasRef.current) {
      paint(canvasRef.current);
    }

    inked.current = false;
    onChange(null);
  }

  return (
    <div>
      <canvas
        ref={canvasRef}
        className="signature-pad"
        width={WIDTH}
        height={HEIGHT}
        aria-label="Recuadro para firmar con el dedo o el mouse"
        onPointerDown={handleDown}
        onPointerMove={handleMove}
        onPointerUp={handleUp}
        onPointerCancel={handleUp}
      />
      <div className="actions">
        <button type="button" className="secondary" onClick={clear}>
          Borrar firma
        </button>
      </div>
    </div>
  );
}