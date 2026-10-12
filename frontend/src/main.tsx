import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import '@fontsource-variable/space-grotesk';
import '@fontsource-variable/jetbrains-mono';
import './styles/theme.css';
import './styles/global.css';
import App from './App';
import { AuthProvider } from './auth/AuthProvider';
import { InstitutionProvider } from './institution/InstitutionProvider';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <InstitutionProvider>
          <App />
        </InstitutionProvider>
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
);