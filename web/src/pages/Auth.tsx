import { QrCode, Ticket, Timer, Users } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { useApp } from "../context";
import { linkProps, navigate } from "../router";

function Intro() {
  return (
    <aside className="auth-intro">
      <a className="brand light" {...linkProps("/")}><span className="brand-mark"><Ticket size={16} /></span>Gişe</a>
      <h2>Tickets for the night out, and for the people running it</h2>
      <ul>
        <li><Users size={18} /><span>Pick your seats on the floor plan. Busy sales open with a fair waiting room instead of a refresh race.</span></li>
        <li><Timer size={18} /><span>Your seats are held while you pay; nobody else can take them, and nothing is ever sold twice.</span></li>
        <li><QrCode size={18} /><span>Signed QR tickets that the gate checks even when the venue's internet is down.</span></li>
      </ul>
    </aside>
  );
}

function useAfter(next: string | undefined) {
  const { signedIn } = useApp();
  return (session: Parameters<typeof signedIn>[0]) => {
    signedIn(session);
    navigate(next ?? "/", true);
  };
}

export function SignIn({ next }: { next?: string }) {
  const done = useAfter(next);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      done(await api.login(email, password));
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  return (
    <div className="auth">
      <Intro />
      <main className="auth-main">
        <form className="auth-card card" onSubmit={submit}>
          <h1>Sign in</h1>
          <label className="field">Email<input className="input" type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus /></label>
          <label className="field">Password<input className="input" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required /></label>
          {error && <p className="form-error" role="alert">{error}</p>}
          <button className="button primary wide" disabled={busy}>{busy ? "Signing in…" : "Sign in"}</button>
          <p className="small muted center-text">New here? <a {...linkProps(`/register${next ? `?next=${encodeURIComponent(next)}` : ""}`)}>Create an account</a></p>
        </form>
      </main>
    </div>
  );
}

export function Register({ next }: { next?: string }) {
  const done = useAfter(next);
  const [form, setForm] = useState({ displayName: "", email: "", password: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [key]: e.target.value });
  const fieldError = (name: string) => error?.fields[name]?.[0];

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      done(await api.register(form));
    } catch (err) {
      setError(err as ApiError);
      setBusy(false);
    }
  }

  return (
    <div className="auth">
      <Intro />
      <main className="auth-main">
        <form className="auth-card card" onSubmit={submit} noValidate>
          <h1>Create an account</h1>
          <label className="field">Your name<input className="input" autoComplete="name" value={form.displayName} onChange={set("displayName")} autoFocus />
            <span className="hint">Printed on your tickets.</span>
            {fieldError("displayName") && <span className="field-error">{fieldError("displayName")}</span>}</label>
          <label className="field">Email<input className="input" type="email" autoComplete="email" value={form.email} onChange={set("email")} />
            {fieldError("email") && <span className="field-error">{fieldError("email")}</span>}</label>
          <label className="field">Password<input className="input" type="password" autoComplete="new-password" value={form.password} onChange={set("password")} />
            <span className="hint">At least 10 characters.</span>
            {fieldError("password") && <span className="field-error">{fieldError("password")}</span>}</label>
          {error && Object.keys(error.fields).length === 0 && <p className="form-error" role="alert">{error.message}</p>}
          <button className="button primary wide" disabled={busy}>{busy ? "Creating…" : "Create account"}</button>
          <p className="small muted center-text">Have an account? <a {...linkProps(`/signin${next ? `?next=${encodeURIComponent(next)}` : ""}`)}>Sign in</a></p>
        </form>
      </main>
    </div>
  );
}
