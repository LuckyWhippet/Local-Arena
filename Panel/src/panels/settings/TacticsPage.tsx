import { useEffect, useState } from "react";
import { Crosshair } from "lucide-react";
import Toggle from "../../components/Toggle";
import { useStore } from "../../state/store";
import { useT } from "../../i18n";
import { tacticsApi, type TacticsSnapshot, type TacticsState } from "../../lib/tacticsApi";
import "./TacticsPage.css";

const MODES: TacticsState["mode"][] = ["per_match", "per_half", "fixed"];

export default function TacticsPage() {
  const { directory, reportError } = useStore();
  const t = useT();
  const csgo = directory?.valid ? directory.selected ?? "" : "";
  const [snapshot, setSnapshot] = useState<TacticsSnapshot | null>(null);
  const [state, setState] = useState<TacticsState | null>(null);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState("");

  useEffect(() => {
    if (!csgo) return;
    let cancelled = false;
    tacticsApi.snapshot(csgo)
      .then(data => { if (!cancelled) { setSnapshot(data); setState(data.state); } })
      .catch(error => { if (!cancelled) reportError(error); });
    return () => { cancelled = true; };
  }, [csgo, reportError]);

  const patch = (next: Partial<TacticsState>) => setState(current => current ? { ...current, ...next } : current);

  const save = async () => {
    if (!csgo || !state || busy) return;
    setBusy(true);
    setSaved("");
    try {
      await tacticsApi.save(csgo, state);
      const data = await tacticsApi.snapshot(csgo);
      setSnapshot(data);
      setState(data.state);
      setSaved(t("tactics.saved"));
    } catch (error) {
      reportError(error);
    } finally {
      setBusy(false);
    }
  };

  if (!csgo) return <div className="tactics-page"><p className="tactics-hint">{t("tactics.noDirectory")}</p></div>;
  if (!snapshot || !state) return <div className="tactics-page"><p className="tactics-hint">{t("stats.loading")}</p></div>;

  const togglePool = (id: string) => {
    const has = state.pool.includes(id);
    patch({ pool: has ? state.pool.filter(item => item !== id) : [...state.pool, id] });
  };

  const removeWaypoint = (name: string) => {
    const next = { ...state.waypoints };
    delete next[name];
    patch({ waypoints: next });
  };

  return (
    <div className="tactics-page">
      <section className="tactics-card">
        <div className="tactics-card__head">
          <Crosshair size={18} />
          <span>
            <strong>{t("tactics.active")}</strong>
            <small>{snapshot.active.tree ?? "engine default"}{snapshot.active.map ? ` · ${snapshot.active.map}` : ""}</small>
          </span>
        </div>
        <p className="tactics-hint">{snapshot.configPath}</p>
        {!snapshot.treeDirExists && <p className="tactics-warn">{t("tactics.noTrees")}</p>}
      </section>

      <section className="tactics-row">
        <span><strong>{t("tactics.enabled")}</strong><small>{t("tactics.enabledDesc")}</small></span>
        <Toggle checked={state.enabled} onChange={next => patch({ enabled: next })} ariaLabel={t("tactics.enabled")} />
      </section>

      <section className="tactics-block">
        <strong>{t("tactics.mode")}</strong>
        <div className="tactics-seg">
          {MODES.map(mode => (
            <button
              key={mode}
              className={state.mode === mode ? "is-active" : ""}
              onClick={() => patch({ mode })}
            >
              {t(`tactics.mode.${mode}` as any)}
            </button>
          ))}
        </div>
      </section>

      <section className="tactics-block">
        <strong>{t("tactics.pool")}</strong>
        <small className="tactics-hint">{t("tactics.poolDesc")}</small>
        <small className="tactics-hint">{t("tactics.selectedCount", { count: state.pool.length })}</small>
        {state.enabled && state.pool.length === 0 && (
          <p className="tactics-warn">{t("tactics.emptyPool")}</p>
        )}
        <div className="tactics-trees">
          {snapshot.trees.map(tree => (
            <label key={tree.id} className="tactics-tree">
              <input
                type="checkbox"
                checked={state.pool.includes(tree.id)}
                onChange={() => togglePool(tree.id)}
                disabled={state.mode === "fixed"}
              />
              <span>
                <b>{tree.id}</b>
                <small>{Math.max(1, Math.round(tree.bytes / 1024))} KB</small>
              </span>
              {state.mode === "fixed" && (
                <button
                  className={state.fixedTree === tree.id ? "tactics-pick is-active" : "tactics-pick"}
                  onClick={() => patch({ fixedTree: tree.id })}
                >
                  {t("tactics.setFixed")}
                </button>
              )}
            </label>
          ))}
          {snapshot.trees.length === 0 && <p className="tactics-hint">{t("tactics.noTrees")}</p>}
        </div>
      </section>

      <section className="tactics-block">
        <strong>{t("tactics.waypoints")}</strong>
        <small className="tactics-hint">{t("tactics.waypointsDesc")}</small>
        <div className="tactics-waypoints">
          {Object.entries(state.waypoints).map(([name, point]) => (
            <div key={name} className="tactics-waypoint">
              <b>{name}</b>
              <span>{point.map(value => Math.round(value)).join(", ")}</span>
              <button className="tactics-pick" onClick={() => removeWaypoint(name)}>{t("tactics.remove")}</button>
            </div>
          ))}
          {Object.keys(state.waypoints).length === 0 && <p className="tactics-hint">{t("tactics.noWaypoints")}</p>}
        </div>
      </section>

      <div className="tactics-actions">
        <button className="tactics-save" disabled={busy} onClick={() => void save()}>
          {busy ? t("stats.saving") : t("stats.save")}
        </button>
        {saved && <span className="tactics-saved">{saved}</span>}
      </div>
    </div>
  );
}
