import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import QRCode from "qrcode";
import "./style.css";

type Quality = "fresh" | "stale" | "unsupported" | "unavailable" | "synthetic";
type Reading = { id: string; value: number | null; unit: string; source: string; quality: Quality; sampledAtUtc: string | null; reason?: string | null };
type Snapshot = { contractVersion: number; adapter: string; isSynthetic: boolean; sessionId: string; sequence: number; capturedAtUtc: string; sampleDurationMs: number; intervalSeconds: number; onAcPower: boolean | null; sensors: Reading[] };
type Incident = { id: string; note: string; markedAtUtc: string; sessionId: string | null; snapshotSequence: number | null; snapshotCapturedAtUtc: string | null };
type Report = { id: string; title: string; body: string; isSynthetic: boolean; adapter: string; createdAtUtc: string };
type Activity = { sequence: number; correlationId: string; kind: string; actor: string; entityId: string; createdAtUtc: string };
type Health = { bridge?: string; collection?: boolean; storage?: string; lastSnapshotAtUtc?: string | null; lastError?: string | null };
type Policy = { storedDefaults: { rawHours: number; aggregateDays: number; reportDays: number; telemetryBudgetMiB: number }; effectiveCollectionEnabled: boolean; cloudEnabled: boolean; rawMaximumRows: number; rawMaximumHours: number };
type Client = { id: string; name: string; pairedAtUtc: string; revokedAtUtc: string | null };
type HostStatus = { phoneAccessEnabled: boolean };
type PairingCode = { code: string; expiresAtUtc: string; phonePort: number; hostAddresses: string[] };
type PairingSession = { clientToken: string; client: Client };
type DashboardData = { snapshot: Snapshot | null; health: Health | null; history: Snapshot[]; incidents: Incident[]; reports: Report[]; events: Activity[]; policy: Policy | null; clients: Client[]; status: HostStatus | null; errors: string[] };

const emptyData: DashboardData = { snapshot: null, health: null, history: [], incidents: [], reports: [], events: [], policy: null, clients: [], status: null, errors: [] };
const sensor = (snapshot: Snapshot | null, id: string): Reading | undefined => snapshot?.sensors.find((reading) => reading.id === id);
const readingText = (reading: Reading | undefined): string => reading?.value === null || reading?.value === undefined ? reading ? `${reading.quality === "unsupported" ? "Unsupported" : "Unavailable"} · ${reading.quality}` : "No sample" : `${reading.value.toFixed(reading.unit === "°C" ? 1 : 0)} ${reading.unit}${reading.quality === "stale" ? " · stale" : ""}`;
const readingStatus = (reading: Reading | undefined): string => reading ? `${reading.quality} · ${stamp(reading.sampledAtUtc)}` : "no sample";
const stamp = (value: string | null | undefined): string => value ? new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "medium" }).format(new Date(value)) : "No reading yet";
const objectValue = (value: unknown): value is Record<string, unknown> => typeof value === "object" && value !== null && !Array.isArray(value);
const isReading = (value: unknown): value is Reading => objectValue(value) && typeof value.id === "string" && (typeof value.value === "number" || value.value === null) && typeof value.unit === "string" && typeof value.source === "string" && ["fresh", "stale", "unsupported", "unavailable", "synthetic"].includes(String(value.quality));
const isSnapshot = (value: unknown): value is Snapshot => objectValue(value) && typeof value.sessionId === "string" && typeof value.sequence === "number" && typeof value.capturedAtUtc === "string" && Array.isArray(value.sensors) && value.sensors.every(isReading) && typeof value.isSynthetic === "boolean";
const isIncident = (value: unknown): value is Incident => objectValue(value) && typeof value.id === "string" && typeof value.note === "string" && typeof value.markedAtUtc === "string" && (typeof value.sessionId === "string" || value.sessionId === null) && (typeof value.snapshotSequence === "number" || value.snapshotSequence === null) && (typeof value.snapshotCapturedAtUtc === "string" || value.snapshotCapturedAtUtc === null);
const isActivity = (value: unknown): value is Activity => objectValue(value) && typeof value.sequence === "number" && typeof value.correlationId === "string" && typeof value.kind === "string" && typeof value.actor === "string" && typeof value.entityId === "string" && typeof value.createdAtUtc === "string";
const isHealth = (value: unknown): value is Health => objectValue(value) && (typeof value.bridge === "string" || value.bridge === undefined) && (typeof value.storage === "string" || value.storage === undefined);
const isPolicy = (value: unknown): value is Policy => objectValue(value) && typeof value.effectiveCollectionEnabled === "boolean" && typeof value.cloudEnabled === "boolean" && typeof value.rawMaximumRows === "number" && typeof value.rawMaximumHours === "number";
const isPairedClient = (value: unknown): value is Client => objectValue(value) && typeof value.id === "string" && typeof value.name === "string" && typeof value.pairedAtUtc === "string" && (typeof value.revokedAtUtc === "string" || value.revokedAtUtc === null);
const isPairingSession = (value: unknown): value is PairingSession => objectValue(value) && typeof value.clientToken === "string" && /^[A-Za-z0-9_-]{43}$/.test(value.clientToken) && isPairedClient(value.client);
const isPrivateIPv4 = (value: unknown): value is string => {
  if (typeof value !== "string") return false;
  const segments = value.split(".");
  if (segments.length !== 4 || segments.some((segment) => !/^\d{1,3}$/.test(segment))) return false;
  const [first, second, third, fourth] = segments.map(Number);
  if ([first, second, third, fourth].some((octet) => octet < 0 || octet > 255)) return false;
  return first === 10 || first === 172 && second >= 16 && second <= 31 || first === 192 && second === 168;
};
const isPairingCode = (value: unknown): value is PairingCode => objectValue(value) && typeof value.code === "string" && /^[A-Za-z0-9_-]{32}$/.test(value.code) && typeof value.expiresAtUtc === "string" && Number.isFinite(Date.parse(value.expiresAtUtc)) && Date.parse(value.expiresAtUtc) > Date.now() && typeof value.phonePort === "number" && Number.isInteger(value.phonePort) && value.phonePort >= 1024 && value.phonePort <= 65535 && Array.isArray(value.hostAddresses) && value.hostAddresses.every(isPrivateIPv4);

async function api<T>(path: string, token: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    cache: "no-store",
    headers: { Authorization: `Bearer ${token}`, ...(init?.body ? { "Content-Type": "application/json" } : {}), ...init?.headers },
  });
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { title?: string } | null;
    throw new Error(response.status === 503 ? "Real telemetry is currently unavailable." : body?.title ?? `Request failed (${response.status}).`);
  }
  const value: unknown = await response.json();
  if (!objectValue(value) && !Array.isArray(value)) throw new Error("The host returned an invalid response.");
  return value as T;
}

function Trend({ title, unit, points, id }: { title: string; unit: string; points: { time: string; value: number }[]; id: string }) {
  const values = points.map((point) => point.value);
  const min = values.length ? Math.min(...values) : 0;
  const max = values.length ? Math.max(...values) : 0;
  const spread = max - min || 1;
  const coords = points.map((point, index) => `${points.length < 2 ? 50 : 8 + index * 84 / (points.length - 1)},${92 - (point.value - min) * 76 / spread}`).join(" ");
  const label = points.length ? `${points.length} real samples, minimum ${min.toFixed(1)} ${unit}, maximum ${max.toFixed(1)} ${unit}` : "No persisted samples in this period";
  return <article className="panel trend-panel">
    <div className="panel-heading"><div><p className="eyebrow">HISTORY</p><h3>{title}</h3></div><span className="range-label">{points.length ? `${min.toFixed(1)}–${max.toFixed(1)} ${unit}` : "No data"}</span></div>
    <svg className="trend" viewBox="0 0 100 100" preserveAspectRatio="none" role="img" aria-label={label}>
      <path className="gridline" d="M0 16H100 M0 50H100 M0 84H100" />
      {points.length > 0 && <polyline className={`trend-line ${id}`} points={coords} />}
    </svg>
    <div className="chart-foot"><span>{points.length ? stamp(points[0].time) : "Waiting for persisted history"}</span><span>{points.length ? stamp(points.at(-1)?.time) : ""}</span></div>
  </article>;
}

function App() {
  const [token, setToken] = useState("");
  const [isOwner, setIsOwner] = useState(false);
  const [tokenInput, setTokenInput] = useState("");
  const [loginError, setLoginError] = useState("");
  const [data, setData] = useState(emptyData);
  const [hours, setHours] = useState(6);
  const [refreshSeconds, setRefreshSeconds] = useState(2);
  const [updatedAt, setUpdatedAt] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [incidentNote, setIncidentNote] = useState("");
  const [incidentError, setIncidentError] = useState("");
  const [pairCode, setPairCode] = useState<string | null>(null);
  const [pairName, setPairName] = useState("Android phone");
  const [pairError, setPairError] = useState("");
  const [pairingBusy, setPairingBusy] = useState(false);
  const [pairingQr, setPairingQr] = useState<string | null>(null);
  const [pairingUrl, setPairingUrl] = useState("");
  const [pairingExpiresAt, setPairingExpiresAt] = useState<string | null>(null);
  const [phoneAddresses, setPhoneAddresses] = useState<string[]>([]);
  const [phonePort, setPhonePort] = useState(5128);
  const [selectedPhoneAddress, setSelectedPhoneAddress] = useState("");
  const [collectionError, setCollectionError] = useState("");
  const generation = useRef(0);
  const refreshInProgress = useRef(false);
  const activeRequest = useRef<AbortController | null>(null);

  useEffect(() => {
    const code = new URLSearchParams(window.location.hash.slice(1)).get("pair");
    if (code) {
      window.history.replaceState(null, "", `${window.location.pathname}${window.location.search}`);
      setPairCode(code);
    }
  }, []);

  const refresh = useCallback(async () => {
    if (!token || refreshInProgress.current) return;
    refreshInProgress.current = true;
    const requestGeneration = generation.current;
    const controller = new AbortController();
    activeRequest.current = controller;
    setLoading(true);
    const paths = ["/v1/telemetry", "/v1/telemetry/health", `/v1/telemetry/range?hours=${hours}`, "/v1/incidents", "/v1/reports", "/v1/events", "/v1/policy", "/v1/status"] as const;
    try {
      const results = await Promise.allSettled(paths.map((path) => api<unknown>(path, token, { signal: controller.signal })));
      if (requestGeneration !== generation.current) return;
      if (results.some((result) => result.status === "rejected" && result.reason instanceof Error && /401|unauthorized/i.test(result.reason.message))) {
        generation.current++;
        setToken("");
        setIsOwner(false);
        setData(emptyData);
        setLoginError("Access token expired or was rejected. Sign in again.");
        return;
      }
      const errors: string[] = [];
      const value = <T,>(index: number, name: string, fallback: T): T => {
        const result = results[index];
        if (result.status === "fulfilled") return result.value as T;
        errors.push(`${name}: ${result.reason instanceof Error ? result.reason.message : "request failed"}`);
        return fallback;
      };
      const snapshotValue = value<unknown>(0, "Telemetry", null);
      const snapshot = isSnapshot(snapshotValue) ? snapshotValue : null;
      if (snapshotValue !== null && snapshot === null) errors.push("Telemetry: the host response did not match the expected snapshot shape.");
      if (snapshot?.isSynthetic) errors.push("The host marked the current snapshot synthetic; it is hidden from this real-data view.");
      const statusValue = value<unknown>(7, "Status", null);
      const status = objectValue(statusValue) && typeof statusValue.phoneAccessEnabled === "boolean" ? statusValue as unknown as HostStatus : null;
      const clientResult = isOwner && status?.phoneAccessEnabled ? await Promise.allSettled([api<unknown>("/v1/pairing/clients", token, { signal: controller.signal })]) : [];
      if (requestGeneration !== generation.current) return;
      const clientsValue = clientResult[0]?.status === "fulfilled" ? clientResult[0].value : [];
      const clients = Array.isArray(clientsValue) ? clientsValue.filter(isPairedClient) : [];
      if (clientResult[0]?.status === "rejected" || !Array.isArray(clientsValue)) errors.push("Phone devices: paired device list is unavailable.");
      const historyValue = value<unknown>(2, "History", []);
      const reportsValue = value<unknown>(4, "Reports", []);
      const incidentsValue = value<unknown>(3, "Incidents", []);
      const eventsValue = value<unknown>(5, "Activity", []);
      const healthValue = value<unknown>(1, "Health", null);
      const policyValue = value<unknown>(6, "Preferences", null);
      setData({
        snapshot,
        health: isHealth(healthValue) ? healthValue : null,
        history: Array.isArray(historyValue) ? historyValue.filter((item): item is Snapshot => isSnapshot(item) && !item.isSynthetic) : [],
        incidents: Array.isArray(incidentsValue) ? incidentsValue.filter(isIncident) : [],
        reports: Array.isArray(reportsValue) ? reportsValue.filter((item): item is Report => objectValue(item) && typeof item.id === "string" && typeof item.title === "string" && typeof item.body === "string" && typeof item.createdAtUtc === "string" && typeof item.isSynthetic === "boolean" && !item.isSynthetic) : [],
        events: Array.isArray(eventsValue) ? eventsValue.filter(isActivity) : [],
        policy: isPolicy(policyValue) ? policyValue : null,
        clients,
        status,
        errors,
      });
      setUpdatedAt(new Date().toISOString());
    } finally {
      if (activeRequest.current === controller) activeRequest.current = null;
      refreshInProgress.current = false;
      if (requestGeneration === generation.current) setLoading(false);
    }
  }, [hours, isOwner, token]);

  useEffect(() => {
    if (!token) return;
    void refresh();
    const timer = window.setInterval(() => void refresh(), refreshSeconds * 1000);
    return () => window.clearInterval(timer);
  }, [refresh, refreshSeconds, token]);

  const charts = useMemo(() => {
    const bySensor = (id: string) => data.history.flatMap((snapshot) => {
      const point = sensor(snapshot, id);
      return point?.quality === "fresh" && point.value !== null && Number.isFinite(point.value)
        ? [{ time: point.sampledAtUtc ?? snapshot.capturedAtUtc, value: point.value }]
        : [];
    });
    return [
      { id: "cpu-temp", title: "CPU temperature", unit: "°C", points: bySensor("cpuTemperature") },
      { id: "cpu-fan", title: "CPU fan speed", unit: "RPM", points: bySensor("cpuFanRpm") },
      { id: "cpu-use", title: "CPU use", unit: "%", points: bySensor("cpuUsagePercent") },
      { id: "battery-rate", title: "Battery rate", unit: "W", points: bySensor("batteryRateWatts") },
    ];
  }, [data.history]);

  const signIn = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const candidate = tokenInput.trim();
    if (candidate.length < 32) { setLoginError("Enter the local access token from the host session."); return; }
    try {
      const session = await api<unknown>("/v1/session", candidate);
      if (!objectValue(session) || (session.role !== "owner" && session.role !== "pairedPhone")) throw new Error("The host returned an invalid session.");
      const owner = session.role === "owner";
      generation.current++;
      setLoginError("");
      setToken(candidate);
      setIsOwner(owner);
      setTokenInput("");
    } catch {
      setLoginError("That token was rejected. Check the local host and try again.");
    }
  };

  const redeemPairing = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!pairCode) return;
    setPairingBusy(true);
    setPairError("");
    try {
      const response = await fetch("/v1/pairing/redeem", { method: "POST", cache: "no-store", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name: pairName, code: pairCode }) });
      if (!response.ok) throw new Error(response.status === 401 ? "The pairing code expired or was already used." : `Pairing failed (${response.status}).`);
      const sessionValue: unknown = await response.json();
      if (!isPairingSession(sessionValue)) throw new Error("The host returned an invalid phone session.");
      const session = sessionValue;
      generation.current++;
      setIsOwner(false);
      setPairCode(null);
      setToken(session.clientToken);
    } catch (error) {
      setPairError(error instanceof Error ? error.message : "Phone pairing failed.");
    } finally {
      setPairingBusy(false);
    }
  };

  const createPhonePairing = async () => {
    setPairError("");
    try {
      const pairingValue = await api<unknown>("/v1/pairing/code", token, { method: "POST" });
      if (!isPairingCode(pairingValue)) throw new Error("The host returned invalid pairing details.");
      const pairing = pairingValue;
      const url = pairing.hostAddresses.length
        ? `https://${pairing.hostAddresses[0]}:${pairing.phonePort}/#pair=${encodeURIComponent(pairing.code)}`
        : "";
      if (!url) throw new Error("No private IPv4 network address is available for phone pairing.");
      const qr = await QRCode.toDataURL(url, { errorCorrectionLevel: "M", margin: 1, width: 220 });
      setPhoneAddresses(pairing.hostAddresses);
      setPhonePort(pairing.phonePort);
      setPairingExpiresAt(pairing.expiresAtUtc);
      setSelectedPhoneAddress(pairing.hostAddresses[0]);
      setPairingUrl(url);
      setPairingQr(qr);
      setPairCode(pairing.code);
    } catch (error) {
      setPairError(error instanceof Error ? error.message : "Could not create a pairing code.");
    }
  };

  const changePhoneAddress = async (address: string) => {
    try {
      const url = `https://${address}:${phonePort}/#pair=${encodeURIComponent(pairCode ?? "")}`;
      const qr = await QRCode.toDataURL(url, { errorCorrectionLevel: "M", margin: 1, width: 220 });
      setSelectedPhoneAddress(address);
      setPairingUrl(url);
      setPairingQr(qr);
    } catch {
      setPairError("Could not update the pairing QR. The existing link and QR remain in use.");
    }
  };

  const copyPairingLink = async () => {
    try { await navigator.clipboard.writeText(pairingUrl); }
    catch { setPairError("Clipboard access is unavailable. Scan the QR code or type the shown link on the phone."); }
  };

  const revokeClient = async (client: Client) => {
    try {
      await api(`/v1/pairing/clients/${client.id}`, token, { method: "DELETE" });
      await refresh();
    } catch (error) {
      setPairError(error instanceof Error ? error.message : "Device could not be revoked.");
    }
  };

  const signOut = () => {
    generation.current++;
    activeRequest.current?.abort();
    setIsOwner(false);
    setToken("");
    setData(emptyData);
  };

  const markIncident = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setIncidentError("");
    try {
      await api<Incident>("/v1/incidents", token, { method: "POST", body: JSON.stringify({ note: incidentNote }) });
      setIncidentNote("");
      await refresh();
    } catch (error) {
      setIncidentError(error instanceof Error ? error.message : "Incident could not be saved.");
    }
  };

  const toggleCollection = async () => {
    if (!data.policy) return;
    setCollectionError("");
    try {
      await api("/v1/policy/collection", token, { method: "POST", body: JSON.stringify({ enabled: !data.policy.effectiveCollectionEnabled }) });
      await refresh();
    } catch (error) {
      setCollectionError(error instanceof Error ? error.message : "Collection preference could not be saved.");
    }
  };

  const live = data.snapshot;
  const freshCount = live?.sensors.filter((item) => item.quality === "fresh" && item.value !== null).length ?? 0;
  const staleCount = live?.sensors.filter((item) => item.quality === "stale").length ?? 0;
  const powerLabel = live?.onAcPower === null || live?.onAcPower === undefined ? "Power source unknown" : live.onAcPower ? "Connected to AC" : "On battery";
  const collectionState = data.policy?.effectiveCollectionEnabled;

  if (!token && pairCode) return <main className="login-shell"><div className="login-card"><div className="brand-mark">TP</div><p className="eyebrow">ONE-USE PHONE PAIRING</p><h1>Connect this phone</h1><p className="muted">This code expires after five minutes and can be used once. The connection is encrypted by the host's trusted local certificate.</p><form onSubmit={(event) => void redeemPairing(event)} className="login-form"><label htmlFor="pair-name">Device name</label><input id="pair-name" value={pairName} maxLength={48} onChange={(event) => setPairName(event.target.value)} required />{pairError && <p className="form-error" role="alert">{pairError}</p>}<button type="submit" className="primary-button" disabled={pairingBusy}>{pairingBusy ? "Pairing…" : "Pair phone"}</button></form><p className="fine-print">If this code expired, create a new one from the owner's dashboard.</p></div></main>;

  if (!token) return <main className="login-shell">
    <div className="login-card">
      <div className="brand-mark">TP</div><p className="eyebrow">LOCAL PC DASHBOARD</p>
      <h1>Sign in to ThermalPilot</h1>
      <p className="muted">Use the access token from the host session. The dashboard displays actual readings and saved local history only.</p>
      <form onSubmit={(event) => void signIn(event)} className="login-form">
        <label htmlFor="token">Local access token</label><input id="token" type="password" autoComplete="current-password" value={tokenInput} onChange={(event) => setTokenInput(event.target.value)} required minLength={32} />
        {loginError && <p className="form-error" role="alert">{loginError}</p>}
        <button type="submit" className="primary-button">Open dashboard</button>
      </form>
      <p className="fine-print">Owner access is local. Phone sessions use a one-use pairing code over trusted HTTPS and can be revoked from this dashboard.</p>
    </div>
  </main>;

  return <div className="app-shell">
    <aside className="sidebar">
      <div className="brand"><div className="brand-mark">TP</div><div><strong>ThermalPilot</strong><span>PC operations hub</span></div></div>
      <nav aria-label="Main navigation"><a href="#overview" className="active">Overview</a><a href="#history">History</a><a href="#incidents">Incidents</a><a href="#activity">Activity</a><a href="#preferences">Preferences</a></nav>
      <div className="sidebar-footer"><span className="dot online" /> Local host connection<br /><span className="small-muted">Phone access {data.status?.phoneAccessEnabled ? "ready" : "disabled"}</span></div>
    </aside>
    <main className="main-content">
      <header className="topbar"><div><p className="eyebrow">THERMALPILOT / PERSONAL PC</p><h1>System overview</h1><p className="muted">Real measurements from the local read-only bridge.</p></div><div className="topbar-actions"><span className={`status-pill ${collectionState === true && live ? "good" : "warn"}`}><span className="dot" />{collectionState === undefined ? "Collection status unavailable" : collectionState ? (live ? "Host connected" : "Waiting for telemetry") : "Collection disabled"}</span><button className="quiet-button" onClick={() => void refresh()} disabled={loading}>{loading ? "Refreshing…" : "Refresh now"}</button><button className="icon-button" onClick={signOut} aria-label="Sign out">↗</button></div></header>
      {data.errors.length > 0 && <section className="notice" role="status"><strong>Some live services are unavailable.</strong><span>{data.errors.join(" · ")}</span></section>}
      <section id="overview" className="overview-grid" aria-label="Current system status">
        <article className="hero-card"><div className="hero-top"><div><p className="eyebrow">CURRENT SESSION</p><h2>{collectionState === undefined ? "Collection status unavailable" : collectionState ? (live ? "Monitoring active" : "No live snapshot") : "Collection disabled"}</h2><p>{live ? `Session ${live.sessionId.slice(0, 8)} · sample ${live.sequence}` : "The host has not provided a real reading yet."}</p></div><span className={`status-pill ${live ? "good" : "warn"}`}><span className="dot" />{live ? "Actual data" : "Unavailable"}</span></div><div className="hero-meta"><span>Updated <strong>{stamp(live?.capturedAtUtc)}</strong></span><span>{powerLabel}</span><span>{freshCount} fresh · {staleCount} stale</span></div></article>
        <article className="metric-card"><span className="metric-icon">°</span><p>CPU temperature</p><strong>{readingText(sensor(live, "cpuTemperature"))}</strong><span className="quality">{sensor(live, "cpuTemperature")?.quality ?? "no sample"}</span></article>
        <article className="metric-card"><span className="metric-icon fan-icon">◌</span><p>CPU fan</p><strong>{readingText(sensor(live, "cpuFanRpm"))}</strong><span className="quality">{readingStatus(sensor(live, "cpuFanRpm"))} · RPM, not PWM percent</span></article>
        <article className="metric-card"><span className="metric-icon">▤</span><p>GPU temperature</p><strong>{readingText(sensor(live, "gpuTemperature"))}</strong><span className="quality">{sensor(live, "gpuTemperature")?.quality ?? "no sample"}</span></article>
        <article className="metric-card"><span className="metric-icon">ϟ</span><p>Battery rate</p><strong>{readingText(sensor(live, "batteryRateWatts"))}</strong><span className="quality">{readingStatus(sensor(live, "batteryRateWatts"))} · negative = discharge</span></article>
      </section>

      <section id="history" className="section-block"><div className="section-heading"><div><p className="eyebrow">OBSERVATIONS</p><h2>Trends and history</h2><p className="muted">Only fresh stored points are plotted. Missing samples stay missing.</p></div><div className="select-group"><label htmlFor="range">Period</label><select id="range" value={hours} onChange={(event) => setHours(Number(event.target.value))}><option value={1}>Last hour</option><option value={6}>Last 6 hours</option><option value={24}>Last 24 hours</option><option value={72}>Last 72 hours</option></select></div></div>
        <div className="chart-grid">{charts.map((chart) => <Trend key={chart.id} {...chart} />)}</div>
        <div className="table-panel"><div className="panel-heading"><div><p className="eyebrow">PERSISTED HISTORY</p><h3>Recent snapshots</h3></div><span className="range-label">{data.history.length} real records</span></div><div className="table-scroll"><table><thead><tr><th>Captured</th><th>CPU temp</th><th>CPU fan</th><th>CPU use</th><th>Battery</th><th>Session</th></tr></thead><tbody>{[...data.history].reverse().slice(0, 20).map((snapshot) => <tr key={`${snapshot.sessionId}-${snapshot.sequence}`}><td>{stamp(snapshot.capturedAtUtc)}</td><td>{readingText(sensor(snapshot, "cpuTemperature"))}</td><td>{readingText(sensor(snapshot, "cpuFanRpm"))}</td><td>{readingText(sensor(snapshot, "cpuUsagePercent"))}</td><td>{readingText(sensor(snapshot, "batteryRateWatts"))}</td><td>{snapshot.sessionId.slice(0, 8)} · {snapshot.sequence}</td></tr>)}</tbody></table>{data.history.length === 0 && <div className="empty-state">No persisted samples in this time range. Collection may be off, the host may have restarted, or the database may not contain that period.</div>}</div></div>
      </section>

      <section className="lower-grid"><article id="incidents" className="panel"><div className="panel-heading"><div><p className="eyebrow">OWNER NOTES</p><h2>Mark an incident</h2></div><span className="badge">Saved locally</span></div><p className="muted">Add a note linked to the latest actual snapshot, when one is available.</p><form className="incident-form" onSubmit={(event) => void markIncident(event)}><label htmlFor="incident-note">What happened?</label><textarea id="incident-note" maxLength={200} value={incidentNote} onChange={(event) => setIncidentNote(event.target.value)} placeholder="Example: fan noise became noticeable during a video call" required /><div className="form-row"><span>{incidentNote.length}/200</span><button className="primary-button" type="submit">Save incident</button></div>{incidentError && <p className="form-error" role="alert">{incidentError}</p>}</form><div className="incident-list">{data.incidents.slice(0, 6).map((incident) => <div className="list-row" key={incident.id}><span className="timeline-dot" /><div><strong>{incident.note}</strong><small>{stamp(incident.markedAtUtc)}{incident.snapshotSequence === null ? " · no live snapshot linked" : ` · sample ${incident.snapshotSequence}`}</small></div></div>)}{data.incidents.length === 0 && <p className="empty-state compact">No incidents have been marked.</p>}</div></article>
        <article id="activity" className="panel"><div className="panel-heading"><div><p className="eyebrow">LOCAL RECORD</p><h2>Reports and activity</h2></div><span className="badge">{data.events.length} events</span></div><div className="activity-list">{data.reports.slice(0, 4).map((report) => <div className="activity-row" key={report.id}><span className="activity-symbol report-symbol">▤</span><div><strong>{report.title}</strong><p>{report.body}</p><small>{stamp(report.createdAtUtc)} · {report.isSynthetic ? "historical synthetic record" : "saved report"}</small></div></div>)}{data.events.slice(0, 8).map((item) => <div className="activity-row" key={item.sequence}><span className="activity-symbol">·</span><div><strong>{item.kind.replaceAll(/([A-Z])/g, " $1")}</strong><p>{item.actor} · record {item.entityId.slice(0, 8)}</p><small>{stamp(item.createdAtUtc)}</small></div></div>)}{data.reports.length + data.events.length === 0 && <p className="empty-state compact">No saved reports or events yet.</p>}</div></article>
      </section>

      <section id="preferences" className="section-block"><div className="section-heading"><div><p className="eyebrow">LOCAL SETTINGS</p><h2>Preferences and access</h2><p className="muted">Collection policy is displayed from the host. Sampling never depends on this page being open.</p></div></div><div className="preferences-grid"><article className="panel preference-panel"><h3>Dashboard refresh</h3><p>Controls browser polling only. It does not change the controller's hardware sampling rate.</p><label htmlFor="refresh">Refresh interval</label><select id="refresh" value={refreshSeconds} onChange={(event) => { const value = Number(event.target.value); setRefreshSeconds(value); try { window.sessionStorage.setItem("tp-refresh-seconds", String(value)); } catch { /* session storage may be unavailable */ } }}><option value={2}>2 seconds</option><option value={5}>5 seconds</option><option value={10}>10 seconds</option></select></article><article className="panel preference-panel"><h3>Telemetry collection</h3><p>{collectionState === undefined ? "The host collection state could not be loaded." : collectionState ? "Enabled on this host." : "Disabled on this host. No new sensor samples are collected while off."}</p>{isOwner && <button className="primary-button" type="button" onClick={() => void toggleCollection()} disabled={!data.policy}>{data.policy?.effectiveCollectionEnabled ? "Turn collection off" : "Turn collection on"}</button>}{collectionError && <p className="form-error" role="alert">{collectionError}</p>}<dl><div><dt>Saved raw history</dt><dd>Up to {data.policy?.rawMaximumHours ?? "—"} hours / {data.policy?.rawMaximumRows ?? "—"} records</dd></div><div><dt>Cloud connection</dt><dd>{data.policy?.cloudEnabled ? "Enabled" : "Off"}</dd></div><div><dt>Latest host health</dt><dd>{data.health?.bridge ?? "Unknown"} · {data.health?.storage ?? "Unknown"}</dd></div></dl></article><article className="panel preference-panel phone-panel"><h3>Phone access</h3>{data.status?.phoneAccessEnabled ? <><p>LAN access uses HTTPS on the configured phone port. On Android, install the ThermalPilot root certificate first, then scan a fresh pairing code.</p>{isOwner ? <><button className="primary-button" onClick={() => void createPhonePairing()}>Create one-use pairing QR</button>{phoneAddresses.length > 1 && <label className="address-select">Phone network address<select value={selectedPhoneAddress} onChange={(event) => void changePhoneAddress(event.target.value)}>{phoneAddresses.map((address) => <option key={address} value={address}>{address}</option>)}</select></label>}{pairingQr && <div className="qr-pair"><img src={pairingQr} alt="One-use ThermalPilot phone pairing QR code" /><div><strong>Scan from Android camera</strong><span>Code expires {stamp(pairingExpiresAt)}</span><small>{pairingUrl}</small><button className="quiet-button" onClick={() => void copyPairingLink()}>Copy phone link</button></div></div>}{pairError && <p className="form-error" role="alert">{pairError}</p>}<div className="paired-list"><strong>Paired devices</strong>{data.clients.map((client) => <div className="paired-row" key={client.id}><span><b>{client.name}</b><small>{client.revokedAtUtc ? `Revoked ${stamp(client.revokedAtUtc)}` : `Paired ${stamp(client.pairedAtUtc)}`}</small></span>{!client.revokedAtUtc && <button className="quiet-button" onClick={() => void revokeClient(client)}>Revoke</button>}</div>)}{data.clients.length === 0 && <small>No phone devices are paired yet.</small>}</div></> : <p>This paired phone can view the dashboard. Pairing controls and device revocation are available to the owner session.</p>}</> : <><p>Phone access is off. Start the host with HTTPS LAN mode and a trusted local certificate to enable pairing.</p><span className="status-pill warn"><span className="dot" />Disabled</span></>}</article></div></section>

      <footer className="page-footer"><span>ThermalPilot · read-only telemetry</span><span>Last dashboard refresh: {stamp(updatedAt)}</span><span>Hardware writes are disabled</span></footer>
    </main>
  </div>;
}

createRoot(document.getElementById("root")!).render(<React.StrictMode><App /></React.StrictMode>);
