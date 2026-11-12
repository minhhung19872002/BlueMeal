import { create } from 'zustand';

export interface AuthUser {
  id: string;
  userName: string;
  fullName: string;
  email?: string | null;
  language: 'vi' | 'en';
  theme: 'light' | 'dark';
  roles: string[];
  permissions: string[];
  mfaEnabled: boolean;
  mfaRequired: boolean;
  mustChangePassword: boolean;
  passwordExpired: boolean;
  warehouseScope?: string[] | null;
  customerScope?: string[] | null;
  /** The current personal-data notice has not been accepted yet (Luật 91/2025). */
  privacyConsentRequired?: boolean;
}

export interface LoginResponse {
  mfaRequired: boolean;
  mfaToken?: string | null;
  mfaMethods?: string[] | null;
  accessToken?: string | null;
  expiresAt?: string | null;
  restricted: boolean;
  mustChangePassword: boolean;
  mfaSetupRequired: boolean;
  user?: AuthUser | null;
}

interface AuthState {
  /** Access token lives in memory only (SPEC §9) — never in localStorage. */
  accessToken: string | null;
  expiresAt: string | null;
  user: AuthUser | null;
  restricted: boolean;
  mustChangePassword: boolean;
  mfaSetupRequired: boolean;
  initialized: boolean;
  setSession: (r: LoginResponse) => void;
  setUser: (u: AuthUser) => void;
  clear: () => void;
  markInitialized: () => void;
  has: (permission: string) => boolean;
  hasAny: (...permissions: string[]) => boolean;
}

export const useAuth = create<AuthState>((set, get) => ({
  accessToken: null,
  expiresAt: null,
  user: null,
  restricted: false,
  mustChangePassword: false,
  mfaSetupRequired: false,
  initialized: false,
  setSession: (r) =>
    set({
      accessToken: r.accessToken ?? null,
      expiresAt: r.expiresAt ?? null,
      user: r.user ?? get().user,
      restricted: r.restricted,
      mustChangePassword: r.mustChangePassword,
      mfaSetupRequired: r.mfaSetupRequired,
    }),
  setUser: (u) => set({ user: u }),
  clear: () => set({ accessToken: null, expiresAt: null, user: null, restricted: false, mustChangePassword: false, mfaSetupRequired: false }),
  markInitialized: () => set({ initialized: true }),
  has: (p) => get().user?.permissions.includes(p) ?? false,
  hasAny: (...ps) => ps.length === 0 || ps.some((p) => get().user?.permissions.includes(p)),
}));

/** Hook returning a stable permission checker bound to the current user. */
export function usePermission() {
  const user = useAuth((s) => s.user);
  const set = new Set(user?.permissions ?? []);
  return {
    has: (p: string) => set.has(p),
    hasAny: (...ps: string[]) => ps.length === 0 || ps.some((p) => set.has(p)),
  };
}
