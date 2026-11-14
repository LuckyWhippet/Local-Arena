import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { Swords } from "lucide-react";
import TacticalBoard from "./TacticalBoard";
import { api, type MatchSession } from "../lib/api";
import { cs2ssMapLabel } from "../data/cs2ssMaps";
import { useT } from "../i18n";
import "./MatchMode.css";

interface LiveMatch {
  matchId: number;
  map: string;
  startedAt: string;
  roundsPlayed: number;
  ctScore: number;
  tScore: number;
  teamAScore: number;
  teamBScore: number;
  aiTree: string | null;
}

interface Props { csgo: string; onExit: () => void }

export default function MatchMode({ csgo, onExit }: Props) {
  const t = useT();
  const [live, setLive] = useState<LiveMatch | null>(null);
  const [session, setSession] = useState<MatchSession | null>(null);
  const [tab, setTab] = useState<"board" | "info">("board");

  useEffect(() => {
    if (!csgo) return;
    let cancelled = false;
    const poll = async () => {
      try {
        const [nextLive, nextSession] = await Promise.all([
          invoke<LiveMatch | null>("get_live_match", { csgo }),
          api.getActiveMatch(csgo).catch(() => null),
        ]);
        if (cancelled) return;
        setLive(nextLive);
        setSession(nextSession ?? null);
      } catch {
        /* the game may not have written a database yet */
      }
    };
    void poll();
    const timer = window.setInterval(() => { void poll(); }, 3000);
    return () => { cancelled = true; window.clearInterval(timer); };
  }, [csgo]);

  const running = !!live || !!session;

  return (
    <div className="mm">
      <div className="mm-head">
        <div className="mm-head__title">
          <span className="mm-head__eyebrow">{t("matchMode.eyebrow")}</span>
          <strong>{live ? cs2ssMapLabel(live.map) : session?.map_id ? cs2ssMapLabel(session.map_id) : t("matchMode.idle")}</strong>
          <small>{live ? `#${live.matchId} · ${live.aiTree ?? "default"}` : session?.opponent_name ?? ""}</small>
        </div>
        <div className="mm-head__score">
          <b>{live ? `${live.teamAScore}:${live.teamBScore}` : "—"}</b>
          <span>{live ? `${t("matchMode.round")} ${live.roundsPlayed + 1}` : ""}</span>
        </div>
        <button className="mm-exit" onClick={onExit}>{t("matchMode.exit")}</button>
      </div>

      <div className="mm-tabs">
        <button className={tab === "board" ? "is-active" : ""} onClick={() => setTab("board")}>{t("tactical.title")}</button>
        <button className={tab === "info" ? "is-active" : ""} onClick={() => setTab("info")}>{t("matchMode.info")}</button>
      </div>

      {tab === "board" && <TacticalBoard csgo={csgo} onBack={() => setTab("info")} />}

      {tab === "info" && (
        <div className="mm-info">
          <div className="mm-info__row"><Swords size={16} /><span>{t("matchMode.session")}</span><b>{session ? session.session_id : "—"}</b></div>
          <div className="mm-info__row"><span>{t("matchMode.opponent")}</span><b>{session?.opponent_name ?? "—"}</b></div>
          <div className="mm-info__row"><span>{t("matchMode.map")}</span><b>{live ? cs2ssMapLabel(live.map) : session?.map_id ?? "—"}</b></div>
          <div className="mm-info__row"><span>{t("matchMode.tree")}</span><b>{live?.aiTree ?? "default"}</b></div>
          <div className="mm-info__row"><span>{t("matchMode.state")}</span><b>{running ? t("matchMode.running") : t("matchMode.idle")}</b></div>
        </div>
      )}
    </div>
  );
}
