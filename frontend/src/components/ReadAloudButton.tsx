import { useEffect, useState } from 'react';
import { useLocation } from 'react-router';
import { usePreferences } from '../preferences/usePreferences';

// A floating button that reads the page with the device's voice. It only exists when the person turned it on.
export function ReadAloudButton() {
  const { preferences } = usePreferences();
  const location = useLocation();
  const [speaking, setSpeaking] = useState(false);

  const supported = typeof window !== 'undefined' && 'speechSynthesis' in window;

  // Leaving the page, or turning the option off, stops the voice.
  useEffect(() => {
    return () => {
      window.speechSynthesis?.cancel();
    };
  }, [location.pathname, preferences.readAloud]);

  if (!supported || !preferences.readAloud) {
    return null;
  }

  function toggle() {
    const synth = window.speechSynthesis;

    if (speaking) {
      synth.cancel();
      return;
    }

    const text = document.querySelector('main')?.innerText?.trim();

    if (!text) {
      return;
    }

    const utterance = new SpeechSynthesisUtterance(text);

    utterance.lang = 'es-CR';
    utterance.onend = () => setSpeaking(false);
    utterance.onerror = () => setSpeaking(false);

    setSpeaking(true);
    synth.speak(utterance);
  }

  return (
    <button type="button" className="read-aloud no-print" onClick={toggle}>
      {speaking ? 'Detener lectura' : 'Leer en voz alta'}
    </button>
  );
}