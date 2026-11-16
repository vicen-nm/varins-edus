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

  // False while the app asks the server whether the browser still has a session to restore.
  ready: boolean;

  login: (username: string, password: string, remember?: boolean) => Promise<void>;
  activate: (input: ActivationInput) => Promise<void>;
  pairDevice: (username: string, code: string) => Promise<void>;
  logout: () => void;
};

export const AuthContext = createContext<AuthContextValue | null>(null);