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
import { AccessReportPage } from './pages/AccessReportPage';
import { ChangePasswordPage } from './pages/ChangePasswordPage';
import { ResetPasswordPage } from './pages/ResetPasswordPage';
import { UsersPage } from './pages/UsersPage';
import { DevicesPage } from './pages/DevicesPage';
import { PairDevicePage } from './pages/PairDevicePage';
import { CardScanPage } from './pages/CardScanPage';
import { MyVehiclesPage } from './pages/MyVehiclesPage';
import { VehiclesReviewPage } from './pages/VehiclesReviewPage';
import { ParkingEventsPage } from './pages/ParkingEventsPage';
import { ParkingScanPage } from './pages/ParkingScanPage';
import { ExitPermitsPage } from './pages/ExitPermitsPage';
import { ExitScanPage } from './pages/ExitScanPage';
import { VisitorFormPage } from './pages/VisitorFormPage';
import { VisitsPage } from './pages/VisitsPage';

export default function App() {
  return (
    <>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/activar/:slug" element={<ActivatePage />} />
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        <Route path="/emparejar" element={<PairDevicePage />} />
        <Route path="/visitas/:slug" element={<VisitorFormPage />} />
        <Route element={<RequireAuth />}>
          <Route path="/" element={<HomePage />} />
          <Route path="/settings/branding" element={<BrandingPage />} />
          <Route path="/students" element={<StudentsPage />} />
          <Route path="/users" element={<UsersPage />} />
          <Route path="/devices" element={<DevicesPage />} />
          <Route path="/settings/password" element={<ChangePasswordPage />} />
          <Route path="/students/import" element={<ImportStudentsPage />} />
          <Route path="/card" element={<CardPage />} />
          <Route path="/cards/scan" element={<CardScanPage />} />
          <Route path="/access/points" element={<AccessPointsPage />} />
          <Route path="/access/manual" element={<AccessManualPage />} />
          <Route path="/access/screen" element={<CafeteriaScreenPage />} />
          <Route path="/access/scan" element={<CafeteriaScanPage />} />
          <Route path="/vehicles" element={<MyVehiclesPage />} />
          <Route path="/vehicles/review" element={<VehiclesReviewPage />} />
          <Route path="/parking/scan" element={<ParkingScanPage />} />
          <Route path="/parking/events" element={<ParkingEventsPage />} />
          <Route path="/exits" element={<ExitPermitsPage />} />
          <Route path="/exits/scan" element={<ExitScanPage />} />
          <Route path="/visits" element={<VisitsPage />} />
          <Route path="/access/events" element={<AccessEventsPage />} />
          <Route path="/access/report" element={<AccessReportPage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>

      <OfflineBanner />
      <UpdatePrompt />
    </>
  );
}