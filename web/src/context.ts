import { createContext, useContext } from "react";
import type { Me, Organization, Session } from "./types";

export interface Account {
  session: Session;
  me: Me;
  /** Reads the organizers again, after one was created or the team changed. */
  refresh: () => Promise<Me>;
  signOut: () => void;
}

export interface AppContext {
  /** Null when signed out: the shop works without an account until someone takes seats. */
  account: Account | null;
  signedIn: (session: Session) => void;
}

export const AppCtx = createContext<AppContext | null>(null);

export function useApp(): AppContext {
  const value = useContext(AppCtx);
  if (!value) throw new Error("useApp outside of the app");
  return value;
}

/** The signed-in person, on pages that require an account. */
export function useAccount(): Account {
  const { account } = useApp();
  if (!account) throw new Error("useAccount on a page that does not require signing in");
  return account;
}

export const OrgCtx = createContext<Organization | null>(null);

export function useOrg(): Organization {
  const value = useContext(OrgCtx);
  if (!value) throw new Error("useOrg outside of an organizer page");
  return value;
}
