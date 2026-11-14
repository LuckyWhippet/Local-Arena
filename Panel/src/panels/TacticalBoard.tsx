import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "../lib/api";
import type {
  Cs2ssBotRole,
  Cs2ssTacticalAggregate,
  Cs2ssTacticalAggregatePath,
  Cs2ssTacticalMatch,
  Cs2ssTacticalMatchData,
  Cs2ssTacticalPath,
} from "../data/cs2ssTypes";
import { radarFor, radarPoint } from "../data/radars";
import { cs2ssMapLabel } from "../data/cs2ssMaps";
import { useStore } from "../state/store";
import { useT, type I18nKey } from "../i18n";
import "./TacticalBoard.css";

const COLORS = ["#7fd4ff", "#6bffb8", "#ffc46b", "#ff8fa3", "#c3a6ff", "#5ee0d0", "#ff9f6b", "#c8ff6b", "#8fb0ff", "#ff6bd6"];
const WINDOW_SECONDS = 30;
const GRENADE_ICON: Record<string, string> = { smoke: "烟", flash: "闪", he: "雷", molotov: "火", decoy: "诱" };

// The board asks for 3 or 5 matches; the Rust command clamps to 1..=10 anyway.
const MATCH_LIMITS = [3, 5] as const;

// A match keeps ai_tree NULL until a tactic tree is applied, and those matches
// run the stock tree. They share the "stock" bucket so the legend does not grow
// a second row for the same behaviour.
const STOCK_TREE = "stock";
const ROLE_UNKNOWN = "Unknown";

type BoardMode = "single" | "aggregate";
type GroupDim = "tree" | "player" | "role" | "winner" | "side";
type SideFilter = "T" | "CT" | "ALL";

const SINGLE_DIMS: GroupDim[] = ["player", "role"];
const AGGREGATE_DIMS: GroupDim[] = ["tree", "player", "role", "winner", "side"];
const SIDES_SINGLE: SideFilter[] = ["T", "CT"];
const SIDES_ALL: SideFilter[] = ["T", "CT", "ALL"];

const GROUP_LABEL: Record<GroupDim, I18nKey> = {
  tree: "tactical.groupTree",
  player: "tactical.groupPlayer",
  role: "tactical.groupRole",
  winner: "tactical.groupWinner",
  side: "tactical.groupSide",
};

const SELECT_STYLE = {
  width: "100%",
  padding: "6px 8px",
  borderRadius: 8,
  background: "rgba(10,18,32,0.9)",
  color: "#cfe0ff",
  border: "1px solid rgba(127,182,255,0.3)",
};

interface Props { csgo: string; onBack: () => void }

function cls(...parts: string[]) {
  return parts.filter(Boolean).join(" ");
}

function fmtDate(iso: string) {
  try {
    const d = new Date(iso);
    const pad = (value: number) => String(value).padStart(2, "0");
    return pad(d.getFullYear()) + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate())
      + " " + pad(d.getHours()) + ":" + pad(d.getMinutes());
  } catch { return iso; }
}

function colorFor(steamId: string, index: number) {
  let hash = 0;
  for (const ch of steamId) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
  return COLORS[(hash + index) % COLORS.length];
}

// Aggregate groups can outnumber the fixed palette, so every group walks the
// hue circle by the golden angle instead of reusing a colour.
function groupColor(index: number) {
  const hue = ((index * 137.508) % 360).toFixed(0);
  return "hsl(" + hue + " 74% 64%)";
}

function aggregateGroupKey(path: Cs2ssTacticalAggregatePath, dim: GroupDim, role: string): string {
  switch (dim) {
    case "tree": return path.aiTree?.trim() || STOCK_TREE;
    case "player": return path.name || path.steamId;
    case "role": return role;
    case "winner": return path.winner?.trim() || "-";
    case "side": return path.team;
  }
}

export default function TacticalBoard({ csgo, onBack }: Props) {
  const t = useT();
  const { reportError } = useStore();
  const [mode, setMode] = useState<BoardMode>("single");
  const [matches, setMatches] = useState<Cs2ssTacticalMatch[]>([]);
  const [matchId, setMatchId] = useState(0);
  const [data, setData] = useState<Cs2ssTacticalMatchData | null>(null);
  const [loading, setLoading] = useState(true);
  const [side, setSide] = useState<SideFilter>("T");
  const [round, setRound] = useState(0);
  const [cursor, setCursor] = useState(WINDOW_SECONDS);
  const [playing, setPlaying] = useState(false);
  const [windowSeconds, setWindowSeconds] = useState(WINDOW_SECONDS);
  const [focusKey, setFocusKey] = useState<string | null>(null);
  const [hidden, setHidden] = useState<Set<string>>(new Set());
  const [singleGroupBy, setSingleGroupBy] = useState<GroupDim>("player");
  const [aggGroupBy, setAggGroupBy] = useState<GroupDim>("tree");
  const [matchLimit, setMatchLimit] = useState<3 | 5>(5);
  const [aggMap, setAggMap] = useState("");
  const [aggRound, setAggRound] = useState<number | "all">("all");
  const [aggregate, setAggregate] = useState<Cs2ssTacticalAggregate | null>(null);
  const [aggLoading, setAggLoading] = useState(false);
  const [hiddenGroups, setHiddenGroups] = useState<Set<string>>(new Set());
  const [botRoles, setBotRoles] = useState<Cs2ssBotRole[]>([]);
  const roundPicked = useRef(false);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    api.listCs2ssTacticalMatches(csgo)
      .then(list => {
        if (cancelled) return;
        setMatches(list);
        setMatchId(list[0]?.matchId ?? 0);
        setAggMap(previous => previous || list[0]?.map || "");
        if (list.length === 0) setLoading(false);
      })
      .catch(error => { if (!cancelled) { reportError(error); setLoading(false); } });
    return () => { cancelled = true; };
  }, [csgo, reportError]);

  // Roles are decoration: an install without bot profiles, or a human player,
  // simply stays in the "Unknown" bucket instead of surfacing an error.
  useEffect(() => {
    let cancelled = false;
    api.getCs2ssBotRoles(csgo)
      .then(list => { if (!cancelled) setBotRoles(list); })
      .catch(() => { if (!cancelled) setBotRoles([]); });
    return () => { cancelled = true; };
  }, [csgo]);

  useEffect(() => {
    if (!matchId) return;
    let cancelled = false;
    setLoading(true);
    api.getCs2ssTacticalMatch(csgo, matchId)
      .then(payload => {
        if (cancelled) return;
        setData(payload);
        setRound(payload.rounds[0]?.roundNumber ?? 0);
        setCursor(WINDOW_SECONDS);
        setLoading(false);
      })
      .catch(error => { if (!cancelled) { reportError(error); setLoading(false); } });
    return () => { cancelled = true; };
  }, [csgo, matchId, reportError]);

  useEffect(() => {
    if (!playing) return;
    const timer = window.setInterval(() => {
      setCursor(value => {
        const next = value + 0.1;
        if (next >= windowSeconds) { setPlaying(false); return windowSeconds; }
        return next;
      });
    }, 100);
    return () => window.clearInterval(timer);
  }, [playing]);

  const maps = useMemo(() => {
    const seen = new Set<string>();
    for (const item of matches) seen.add(item.map);
    return [...seen];
  }, [matches]);

  useEffect(() => {
    if (maps.length === 0) return;
    if (!aggMap || !maps.includes(aggMap)) setAggMap(maps[0]);
  }, [maps, aggMap]);

  useEffect(() => {
    if (mode !== "aggregate" || !aggMap) return;
    let cancelled = false;
    setAggLoading(true);
    api.getCs2ssTacticalAggregate(csgo, aggMap, matchLimit)
      .then(payload => {
        if (cancelled) return;
        setAggregate(payload);
        setAggLoading(false);
      })
      .catch(error => { if (!cancelled) { reportError(error); setAggLoading(false); } });
    return () => { cancelled = true; };
  }, [csgo, mode, aggMap, matchLimit, reportError]);

  useEffect(() => { setHiddenGroups(new Set()); }, [aggGroupBy, mode, aggMap, matchLimit]);

  const single = mode === "single" ? data : null;
  const boardMap = single ? single.map : (aggregate?.map ?? aggMap);
  const radar = boardMap ? radarFor(boardMap) : null;

  const roleByName = useMemo(() => {
    const found = new Map<string, string>();
    for (const role of botRoles) found.set(role.name, role.role);
    return found;
  }, [botRoles]);

  const roleNames = useMemo(() => {
    const names = new Set<string>();
    for (const role of botRoles) names.add(role.role);
    names.add(ROLE_UNKNOWN);
    return [...names].sort((left, right) => left.localeCompare(right));
  }, [botRoles]);

  const roleColors = useMemo(
    () => new Map(roleNames.map((name, index) => [name, groupColor(index)] as [string, string])),
    [roleNames],
  );

  const roleName = (name: string) => roleByName.get(name) ?? ROLE_UNKNOWN;
  const roleLabel = (role: string) => (role === ROLE_UNKNOWN ? t("tactical.roleUnknown") : role);

  const aggRounds = useMemo(() => {
    const seen = new Set<number>();
    for (const path of aggregate?.paths ?? []) seen.add(path.roundNumber);
    return [...seen].sort((left, right) => left - right);
  }, [aggregate]);

  useEffect(() => {
    if (roundPicked.current || aggRounds.length === 0) return;
    setAggRound(aggRounds[0]);
  }, [aggRounds]);

  const effectiveAggRound: number | "all" = aggRound === "all"
    ? "all"
    : (aggRounds.includes(aggRound) ? aggRound : (aggRounds[0] ?? "all"));

  const aggPaths = useMemo(() => {
    if (!aggregate) return [];
    return aggregate.paths.filter(path =>
      (effectiveAggRound === "all" || path.roundNumber === effectiveAggRound)
      && (side === "ALL" || path.team === side));
  }, [aggregate, effectiveAggRound, side]);

  const groupRole = useMemo(() => {
    const found = new Map<string, string>();
    for (const path of aggPaths) found.set(path.name, roleByName.get(path.name) ?? ROLE_UNKNOWN);
    return found;
  }, [aggPaths, roleByName]);

  const groups = useMemo(() => {
    const buckets = new Map<string, { key: string; count: number; color: string }>();
    for (const path of aggPaths) {
      const key = aggregateGroupKey(path, aggGroupBy, groupRole.get(path.name) ?? ROLE_UNKNOWN);
      const bucket = buckets.get(key);
      if (bucket) bucket.count += 1;
      else buckets.set(key, { key, count: 1, color: "" });
    }
    const list = [...buckets.values()].sort(
      (left, right) => right.count - left.count || left.key.localeCompare(right.key),
    );
    list.forEach((group, index) => {
      group.color = aggGroupBy === "role" ? (roleColors.get(group.key) ?? groupColor(index)) : groupColor(index);
    });
    return list;
  }, [aggPaths, aggGroupBy, groupRole, roleColors]);

  const groupColors = useMemo(
    () => new Map(groups.map(group => [group.key, group.color] as [string, string])),
    [groups],
  );

  // Legend counts describe the current round/side slice; hiding a group only
  // changes what is drawn, never the bucket it belongs to.
  const aggVisiblePaths = useMemo(
    () => aggPaths.filter(path =>
      !hiddenGroups.has(aggregateGroupKey(path, aggGroupBy, groupRole.get(path.name) ?? ROLE_UNKNOWN))),
    [aggPaths, aggGroupBy, groupRole, hiddenGroups],
  );

  const visiblePaths = useMemo(() => {
    if (!single) return [];
    return single.paths.filter(path => path.roundNumber === round && path.team === side && !hidden.has(path.steamId));
  }, [single, round, side, hidden]);

  const players = useMemo(() => {
    if (!single) return [];
    const seen = new Map<string, { steamId: string; name: string; team: string }>();
    for (const path of single.paths) {
      if (path.roundNumber !== round || path.team !== side) continue;
      if (!seen.has(path.steamId)) seen.set(path.steamId, { steamId: path.steamId, name: path.name, team: path.team });
    }
    return [...seen.values()];
  }, [single, round, side]);

  const singlePathColor = (path: Cs2ssTacticalPath, index: number) =>
    singleGroupBy === "role"
      ? (roleColors.get(roleName(path.name)) ?? "#7fb6ff")
      : colorFor(path.steamId, index);

  const switchMode = (next: BoardMode) => {
    setMode(next);
    setPlaying(false);
    setCursor(WINDOW_SECONDS);
    if (next === "single" && side === "ALL") setSide("T");
  };

  if (loading) return <div className="tac"><div className="tac-loading">{t("stats.loading")}</div></div>;
  if (matches.length === 0) {
    return (
      <div className="tac">
        <button className="stats-back" onClick={onBack}>← {t("stats.back")}</button>
        <div className="tac-empty">{t("tactical.empty")}</div>
      </div>
    );
  }
  if (mode === "aggregate" && aggLoading && !aggregate) {
    return <div className="tac"><div className="tac-loading">{t("stats.loading")}</div></div>;
  }
  if (mode === "single" && !single) {
    return <div className="tac"><div className="tac-empty">{t("tactical.empty")}</div></div>;
  }

  const activeDims = mode === "aggregate" ? AGGREGATE_DIMS : SINGLE_DIMS;
  const activeGroupBy = mode === "aggregate" ? aggGroupBy : singleGroupBy;
  const selectGroup = (dim: GroupDim) => {
    if (mode === "aggregate") setAggGroupBy(dim);
    else setSingleGroupBy(dim);
  };

  const currentRound = single?.rounds.find(item => item.roundNumber === round);
  const score = currentRound ? currentRound.teamAScore + ":" + currentRound.teamBScore : "0:0";
  const deathMarks = single ? single.deaths.filter(death => death.roundNumber === round && death.atSeconds <= cursor) : [];
  const grenadeMarks = single ? single.grenades.filter(item => item.roundNumber === round && item.detonatedSeconds <= cursor) : [];

  const sideLabel = side === "ALL" ? t("tactical.sideAll") : side + "-SIDE";
  const roundLabel = single
    ? t("tactical.round") + " " + (round + 1)
    : effectiveAggRound === "all"
      ? t("tactical.roundAll")
      : t("tactical.round") + " " + (effectiveAggRound + 1);
  const headerMeta = single
    ? t("tactical.match") + " #" + single.matchId + " · " + fmtDate(single.startedAt) + " · " + (single.aiTree ?? STOCK_TREE)
    : t("tactical.summary", { paths: aggPaths.length, matches: aggregate?.matches.length ?? 0 })
      + " · " + t("tactical.group") + ": " + t(GROUP_LABEL[aggGroupBy]);

  return (
    <div className="tac">
      <button className="stats-back" onClick={onBack}>← {t("stats.back")}</button>

      <div className="tac-head">
        <div className="tac-head__title">
          <span className="tac-head__eyebrow">{t("tactical.title")}</span>
          <span className="tac-head__map">{cs2ssMapLabel(boardMap)}</span>
          <span className="tac-head__meta">{headerMeta}</span>
        </div>
        <div className="tac-head__score">
          {single ? (
            <>
              <b>{score}</b>
              <span className="tac-head__clock">HALF-TIME {cursor.toFixed(0).padStart(2, "0")}s</span>
            </>
          ) : (
            <>
              <b>{aggVisiblePaths.length}</b>
              <span className="tac-head__meta">{t("tactical.paths")}</span>
            </>
          )}
        </div>
      </div>

      <div className="tac-controls">
        <span className="tac-controls__label">{t("tactical.source")}</span>
        <button
          className={cls("tac-btn", "tac-btn--sm", mode === "single" ? "is-active" : "")}
          onClick={() => switchMode("single")}
        >
          {t("tactical.sourceSingle")}
        </button>
        <button
          className={cls("tac-btn", "tac-btn--sm", mode === "aggregate" ? "is-active" : "")}
          onClick={() => switchMode("aggregate")}
        >
          {t("tactical.sourceAggregate")}
        </button>
        {mode === "aggregate"
          ? MATCH_LIMITS.map(value => (
            <button
              key={value}
              className={cls("tac-btn", "tac-btn--sm", matchLimit === value ? "is-active" : "")}
              onClick={() => setMatchLimit(value)}
            >
              {value}
            </button>
          ))
          : null}

        <span className="tac-controls__sep" />
        <span className="tac-controls__label">{t("tactical.group")}</span>
        {activeDims.map(dim => (
          <button
            key={dim}
            className={cls("tac-btn", "tac-btn--sm", activeGroupBy === dim ? "is-active" : "")}
            onClick={() => selectGroup(dim)}
          >
            {t(GROUP_LABEL[dim])}
          </button>
        ))}

        <span className="tac-controls__sep" />
        {(mode === "aggregate" ? SIDES_ALL : SIDES_SINGLE).map(value => (
          <button
            key={value}
            className={cls("tac-btn", "tac-btn--sm", side === value ? "is-active" : "")}
            onClick={() => setSide(value)}
          >
            {value === "ALL" ? t("tactical.sideAll") : value + "-SIDE"}
          </button>
        ))}

        <button className="tac-btn" onClick={() => { setCursor(0); setPlaying(true); }}>{t("tactical.play")}</button>
        <button className="tac-btn" onClick={() => setPlaying(false)}>{t("tactical.pause")}</button>
        <input
          className="tac-slider"
          type="range" min={0} max={windowSeconds} step={0.1} value={cursor}
          onChange={event => { setPlaying(false); setCursor(Number(event.target.value)); }}
        />
        <span className="tac-head__meta">{cursor.toFixed(1)}s / {windowSeconds}s</span>
        <span className="tac-controls__sep" />
        {[10, 20, 30].map(seconds => (
          <button
            key={seconds}
            className={cls("tac-btn", "tac-btn--sm", windowSeconds === seconds ? "is-active" : "")}
            onClick={() => { setWindowSeconds(seconds); setCursor(value => Math.min(value, seconds)); }}
          >
            {seconds}s
          </button>
        ))}
      </div>

      <div className="tac-body">
        <div className="tac-radar">
          {radar ? (
            <svg viewBox="0 0 1024 1024">
              <image href={radar.image} x={0} y={0} width={1024} height={1024} opacity={0.92} />
              {single ? (
                <>
                  {grenadeMarks.map((grenade, index) => {
                    const from = radarPoint(boardMap, grenade.from[0], grenade.from[1]);
                    const to = radarPoint(boardMap, grenade.to[0], grenade.to[1]);
                    if (!from || !to) return null;
                    const controlX = (from[0] + to[0]) / 2;
                    const controlY = Math.min(from[1], to[1]) - 40;
                    const color = grenade.team === side ? "#ffd64a" : "#8aa0c4";
                    return (
                      <g key={"g-" + index} opacity={0.75}>
                        <path
                          d={"M " + from[0] + " " + from[1] + " Q " + controlX + " " + controlY + " " + to[0] + " " + to[1]}
                          stroke={color} strokeWidth={2} fill="none" strokeDasharray="6 5"
                        />
                        <circle cx={to[0]} cy={to[1]} r={6} fill="none" stroke={color} strokeWidth={2} />
                        <text x={to[0]} y={to[1] - 9} fill={color} fontSize={12} fontWeight={700} textAnchor="middle">
                          {GRENADE_ICON[grenade.kind] ?? "?"}
                        </text>
                      </g>
                    );
                  })}
                  {visiblePaths.map((path, index) => {
                    const color = singlePathColor(path, index);
                    const focus = singleGroupBy === "role" ? roleName(path.name) : path.steamId;
                    const dimmed = focusKey !== null && focusKey !== focus;
                    const limit = Math.max(2, Math.floor(cursor * path.sampleHz));
                    const screen = path.points
                      .slice(0, limit)
                      .map(point => radarPoint(boardMap, point[0], point[1]))
                      .filter((point): point is [number, number] => point !== null);
                    if (screen.length < 2) return null;
                    const head = screen[screen.length - 1];
                    return (
                      <g key={path.steamId}>
                        <polyline
                          points={screen.map(point => point[0].toFixed(1) + "," + point[1].toFixed(1)).join(" ")}
                          fill="none" stroke={color} strokeWidth={dimmed ? 2 : 3} strokeLinejoin="round" strokeLinecap="round" opacity={dimmed ? 0.15 : 0.92}
                        />
                        <circle cx={screen[0][0]} cy={screen[0][1]} r={4} fill={color} opacity={0.8} />
                        <circle cx={head[0]} cy={head[1]} r={6} fill={color} />
                        <text x={head[0] + 9} y={head[1] + 4} fill={color} fontSize={13} fontWeight={700}>{path.name}</text>
                      </g>
                    );
                  })}
                  {deathMarks.map((death, index) => {
                    const point = radarPoint(boardMap, death.position[0], death.position[1]);
                    if (!point) return null;
                    return (
                      <g key={"d-" + index} opacity={0.95}>
                        <line x1={point[0] - 6} y1={point[1] - 6} x2={point[0] + 6} y2={point[1] + 6} stroke="#ff5a5a" strokeWidth={2.5} />
                        <line x1={point[0] - 6} y1={point[1] + 6} x2={point[0] + 6} y2={point[1] - 6} stroke="#ff5a5a" strokeWidth={2.5} />
                      </g>
                    );
                  })}
                </>
              ) : (
                aggVisiblePaths.map(path => {
                  const role = groupRole.get(path.name) ?? ROLE_UNKNOWN;
                  const groupKey = aggregateGroupKey(path, aggGroupBy, role);
                  const color = groupColors.get(groupKey) ?? "#7fb6ff";
                  const dimmed = focusKey !== null && focusKey !== groupKey;
                  const limit = Math.max(2, Math.floor(cursor * path.sampleHz));
                  const screen = path.points
                    .slice(0, limit)
                    .map(point => radarPoint(boardMap, point[0], point[1]))
                    .filter((point): point is [number, number] => point !== null);
                  if (screen.length < 2) return null;
                  const head = screen[screen.length - 1];
                  return (
                    <g key={path.matchId + "-" + path.roundNumber + "-" + path.steamId} opacity={dimmed ? 0.1 : 0.55}>
                      <title>{path.name + " · R" + (path.roundNumber + 1) + " · #" + path.matchId}</title>
                      <polyline
                        points={screen.map(point => point[0].toFixed(1) + "," + point[1].toFixed(1)).join(" ")}
                        fill="none" stroke={color} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round"
                      />
                      <circle cx={head[0]} cy={head[1]} r={3.5} fill={color} />
                    </g>
                  );
                })
              )}
            </svg>
          ) : (
            <div className="tac-radar__empty">{t("tactical.noRadar")}</div>
          )}
          {!single && aggPaths.length === 0 ? (
            <div className="tac-radar__empty">{t("tactical.aggregateEmpty")}</div>
          ) : null}
          <div className="tac-lower">
            <b>{t("tactical.title")}</b>
            <span>{cs2ssMapLabel(boardMap)} · {sideLabel}</span>
            <span>· {roundLabel}</span>
          </div>
        </div>

        <div className="tac-side">
          {single ? (
            <>
              <div className="tac-panel">
                <div className="tac-panel__title">{t("tactical.round")}</div>
                <div className="tac-rounds">
                  {single.rounds.map(item => (
                    <div
                      key={item.roundNumber}
                      className={cls("tac-round", item.roundNumber === round ? "is-active" : "", item.winnerTeam === "CT" ? "ct" : "t")}
                      onClick={() => { setRound(item.roundNumber); setPlaying(false); }}
                    >
                      {item.roundNumber + 1}
                    </div>
                  ))}
                </div>
              </div>

              <div className="tac-panel">
                <div className="tac-panel__title">{t("tactical.players")}</div>
                <div className="tac-legend">
                  {players.map((player, index) => {
                    const role = roleName(player.name);
                    const color = singleGroupBy === "role" ? (roleColors.get(role) ?? "#7fb6ff") : colorFor(player.steamId, index);
                    return (
                      <div
                        key={player.steamId}
                        className={cls("tac-legend__row", hidden.has(player.steamId) ? "is-off" : "")}
                        onMouseEnter={() => setFocusKey(singleGroupBy === "role" ? roleName(player.name) : player.steamId)}
                        onMouseLeave={() => setFocusKey(null)}
                        onClick={() => setHidden(previous => {
                          const next = new Set(previous);
                          if (next.has(player.steamId)) next.delete(player.steamId);
                          else next.add(player.steamId);
                          return next;
                        })}
                      >
                        <span className="tac-legend__dot" style={{ background: color }} />
                        <span className="tac-legend__name">{player.name}</span>
                        <span className="tac-legend__team">{singleGroupBy === "role" ? "· " + roleLabel(role) : player.team}</span>
                      </div>
                    );
                  })}
                </div>
              </div>

              <div className="tac-panel">
                <div className="tac-panel__title">{t("tactical.match")}</div>
                <select value={matchId} onChange={event => setMatchId(Number(event.target.value))} style={SELECT_STYLE}>
                  {matches.map(item => (
                    <option key={item.matchId} value={item.matchId}>
                      {"#" + item.matchId + " " + cs2ssMapLabel(item.map) + " " + fmtDate(item.startedAt)}
                    </option>
                  ))}
                </select>
              </div>
            </>
          ) : (
            <>
              <div className="tac-panel">
                <div className="tac-panel__title">{t("tactical.map")}</div>
                <select value={aggMap} onChange={event => setAggMap(event.target.value)} style={SELECT_STYLE}>
                  {maps.map(map => (
                    <option key={map} value={map}>{cs2ssMapLabel(map)}</option>
                  ))}
                </select>
              </div>

              <div className="tac-panel">
                <div className="tac-panel__title">{t("tactical.round")}</div>
                <button
                  className={cls("tac-btn", "tac-btn--sm", "tac-btn--block", effectiveAggRound === "all" ? "is-active" : "")}
                  onClick={() => { roundPicked.current = true; setAggRound("all"); setPlaying(false); }}
                >
                  {t("tactical.roundAll")}
                </button>
                <div className="tac-rounds">
                  {aggRounds.map(item => (
                    <div
                      key={item}
                      className={cls("tac-round", effectiveAggRound === item ? "is-active" : "")}
                      onClick={() => { roundPicked.current = true; setAggRound(item); setPlaying(false); }}
                    >
                      {item + 1}
                    </div>
                  ))}
                </div>
              </div>

              <div className="tac-panel">
                <div className="tac-panel__title">{t("tactical.groups")}</div>
                <div className="tac-legend">
                  {groups.map(group => (
                    <div
                      key={group.key}
                      className={cls("tac-legend__row", hiddenGroups.has(group.key) ? "is-off" : "")}
                    onMouseEnter={() => setFocusKey(group.key)}
                    onMouseLeave={() => setFocusKey(null)}
                      onClick={() => setHiddenGroups(previous => {
                        const next = new Set(previous);
                        if (next.has(group.key)) next.delete(group.key);
                        else next.add(group.key);
                        return next;
                      })}
                    >
                      <span className="tac-legend__dot" style={{ background: group.color }} />
                      <span className="tac-legend__name">
                        {aggGroupBy === "role" ? roleLabel(group.key) : group.key}
                      </span>
                      <span className="tac-legend__count">{group.count}</span>
                    </div>
                  ))}
                </div>
              </div>

              <div className="tac-panel">
                <div className="tac-panel__title">{t("tactical.matches") + " · " + matchLimit}</div>
                <div className="tac-matchlist">
                  {(aggregate?.matches ?? []).map(item => (
                    <div key={item.matchId} className="tac-matchlist__row">
                      <b>{"#" + item.matchId}</b>
                      <span>{fmtDate(item.startedAt)}</span>
                      <span>{item.aiTree ?? STOCK_TREE}</span>
                      <span>{item.rounds + " " + t("tactical.rounds")}</span>
                    </div>
                  ))}
                </div>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
