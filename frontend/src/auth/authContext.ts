import { createContext } from 'react';
import type { UserInfo } from '../api/types';

export type ActivationInput = {
  institutionSlug: string;
  identificationNumber: string;
  code: string;
  password: string;
};

export type AuthContextValue = {
  user: UserInfo | null;
  login: (username: string, password: string) => Promise<void>;
  activate: (input: ActivationInput) => Promise<void>;
  logout: () => void;
};

export const AuthContext = createContext<AuthContextValue | null>(null);