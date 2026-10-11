import { createContext } from 'react';
import type { UserInfo } from '../api/types';

export type AuthContextValue = {
  user: UserInfo | null;
  login: (username: string, password: string) => Promise<void>;
  logout: () => void;
};

export const AuthContext = createContext<AuthContextValue | null>(null);