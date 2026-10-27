export type UserInfo = {
  userId: string;
  username: string;
  institutionId: string | null;
  isPlatform: boolean;
  permissions: string[];
};

export type LoginResponse = {
  accessToken: string;
  expiresAt: string;
  user: UserInfo;
};

export type InstitutionModule = {
  moduleKey: string;
  isEnabled: boolean;
};

export type InstitutionDetails = {
  id: string;
  name: string;
  slug: string;
  timeZone: string;
  locale: string;
  settings: {
    displayName: string | null;
    logoUrl: string | null;
    primaryColor: string | null;
    accentColor: string | null;
  } | null;
  modules: InstitutionModule[];
};

export type PublicInstitution = {
  name: string;
  primaryColor: string | null;
  accentColor: string | null;
};

export type StudentItem = {
  personId: string;
  identificationNumber: string;
  firstName: string;
  lastName: string;
  groupId: string | null;
  group: string | null;
  hasAccount: boolean;
  codeStatus: 'none' | 'pending' | 'expired' | 'locked' | 'used';
  codeExpiresAt: string | null;
};

export type StudentList = {
  total: number;
  items: StudentItem[];
};

export type GroupItem = {
  id: string;
  name: string;
};

export type RegeneratedCode = {
  code: string;
  expiresAt: string;
};

export type RowError = { rowNumber: number; code: string };

export type ImportPreview = {
  headers: string[];
  suggestedMapping: Record<string, string>;
  mapping: Record<string, string>;
  missingFields: string[];
  totalRows: number;
  sample: string[][];
  summary: {
    newStudents: number;
    existingStudents: number;
    errorRows: number;
    errors: RowError[];
    groupsToCreate: string[];
  } | null;
};

export type IssuedCode = {
  identificationNumber: string;
  firstName: string;
  lastName: string;
  group: string | null;
  code: string;
};

export type ImportResult = {
  batchId: string;
  created: number;
  alreadyExisted: number;
  errorRows: number;
  errors: RowError[];
  codes: IssuedCode[];
};

export type StudentCard = {
  firstName: string;
  lastName: string;
  group: string | null;
  token: string;
  expiresAt: string;
};

export type AccessPoint = {
  id: string;
  name: string;
  kind: 'cafeteria' | 'gate';
  latitude: number | null;
  longitude: number | null;
  radiusMeters: number;
  maxAccuracyMeters: number;
  isActive: boolean;
};

export type AccessService = {
  id: string;
  accessPointId: string;
  name: string;
  startTime: string;
  endTime: string;
  isActive: boolean;
};

export type RegistrationResult = {
  firstName: string;
  lastName: string;
  pointName: string;
  serviceName: string;
  occurredAt: string;
};