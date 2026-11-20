import { useState } from 'react';
import type { FormEvent } from 'react';
import type { DisplayPreferences, UiMode } from '../api/types';
import { clampFontScale, fontScaleLabel } from '../preferences/preferencesRules';

type Props = {
  initial: DisplayPreferences;
  busy: boolean;
  submitLabel: string;
  onSave: (value: DisplayPreferences) => void;
};

// The same form for a person's own screen and for a student's, set by the staff.
export function PreferencesForm({ initial, busy, submitLabel, onSave }: Props) {
  const [uiMode, setUiMode] = useState<UiMode>(initial.uiMode);
  const [fontScale, setFontScale] = useState(initial.fontScale);
  const [highContrast, setHighContrast] = useState(initial.highContrast);
  const [reducedMotion, setReducedMotion] = useState(initial.reducedMotion);
  const [readAloud, setReadAloud] = useState(initial.readAloud);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onSave({ uiMode, fontScale: clampFontScale(fontScale), readAloud, highContrast, reducedMotion });
  }

  return (
    <form onSubmit={handleSubmit} noValidate>
      <label htmlFor="pref-mode">Interfaz</label>
      <select id="pref-mode" value={uiMode} onChange={(e) => setUiMode(e.target.value as UiMode)}>
        <option value="standard">Estándar</option>
        <option value="simple">Simple: botones grandes y menos información</option>
      </select>

      <label htmlFor="pref-scale">Tamaño de letra: {fontScaleLabel(fontScale)}</label>
      <input
        id="pref-scale"
        type="range"
        min={1}
        max={2}
        step={0.1}
        value={fontScale}
        onChange={(e) => setFontScale(Number(e.target.value))}
      />

      <label className="check" htmlFor="pref-contrast">
        <input
          id="pref-contrast"
          type="checkbox"
          checked={highContrast}
          onChange={(e) => setHighContrast(e.target.checked)}
        />
        Alto contraste
      </label>

      <label className="check" htmlFor="pref-motion">
        <input
          id="pref-motion"
          type="checkbox"
          checked={reducedMotion}
          onChange={(e) => setReducedMotion(e.target.checked)}
        />
        Menos movimiento
      </label>

      <label className="check" htmlFor="pref-read">
        <input id="pref-read" type="checkbox" checked={readAloud} onChange={(e) => setReadAloud(e.target.checked)} />
        Botón para leer la pantalla en voz alta
      </label>

      <div className="actions">
        <button type="submit" disabled={busy}>
          {busy ? 'Guardando…' : submitLabel}
        </button>
      </div>
    </form>
  );
}