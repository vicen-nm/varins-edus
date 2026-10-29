import { Navigate, Route, Routes } from 'react-router';
import { RequireAuth } from './auth/RequireAuth';
import { ActivatePage } from './pages/ActivatePage';
import { BrandingPage } from './pages/BrandingPage';
import { HomePage } from './pages/HomePage';
import { LoginPage } from './pages/LoginPage';
import { StudentsPage } from './pages/StudentsPage';
import { OfflineBanner } from './pwa/OfflineBanner';
import { UpdatePrompt } from './pwa/UpdatePrompt';
import { ImportStudentsPage } from './pages/ImportStudentsPage';
import { CardPage } from './pages/CardPage';
import { AccessManualPage } from './pages/AccessManualPage';
import { AccessPointsPage } from './pages/AccessPointsPage';
import { AccessEventsPage } from './pages/AccessEventsPage';
import { CafeteriaScanPage } from './pages/CafeteriaScanPage';
import { CafeteriaScreenPage } from './pages/CafeteriaScreenPage';

export default function App() {
  return (
    <>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/activar/:slug" element={<ActivatePage />} />
        <Route element={<RequireAuth />}>
          <Route path="/" element={<HomePage />} />
          <Route path="/settings/branding" element={<BrandingPage />} />
          <Route path="/students" element={<StudentsPage />} />
          <Route path="/students/import" element={<ImportStudentsPage />} />
          <Route path="/card" element={<CardPage />} />
          <Route path="/access/points" element={<AccessPointsPage />} />
          <Route path="/access/manual" element={<AccessManualPage />} />
          <Route path="/access/screen" element={<CafeteriaScreenPage />} />
          <Route path="/access/scan" element={<CafeteriaScanPage />} />
          <Route path="/access/events" element={<AccessEventsPage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>

      <OfflineBanner />
      <UpdatePrompt />
    </>
  );
}