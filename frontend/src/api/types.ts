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