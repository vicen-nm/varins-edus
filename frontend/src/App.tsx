import { Navigate, Route, Routes } from 'react-router';
import { RequireAuth } from './auth/RequireAuth';
import { BrandingPage } from './pages/BrandingPage';
import { HomePage } from './pages/HomePage';
import { LoginPage } from './pages/LoginPage';
import { OfflineBanner } from './pwa/OfflineBanner';
import { UpdatePrompt } from './pwa/UpdatePrompt';

export default function App() {
  return (
    <>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<RequireAuth />}>
          <Route path="/" element={<HomePage />} />
          <Route path="/settings/branding" element={<BrandingPage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>

      <OfflineBanner />
      <UpdatePrompt />
    </>
  );
}