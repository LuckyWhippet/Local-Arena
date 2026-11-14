export interface Cs2ssPlayerOverview {
  steamId: string;
  name: string;
  matches: number;
  kills: number;
  deaths: number;
  assists: number;
  damage: number;
  headshots: number;
  totalRounds: number;
  kastRounds: number;
  tradeKills: number;
  multikill2: number;
  multikill3: number;
  multikill4: number;
  multikill5: number;
  clutchAttempts: number;
  clutchesWon: number;
}

export interface Cs2ssOverviewResponse {
  matchCount: number;
  players: Cs2ssPlayerOverview[];
}

export interface Cs2ssMatchSummary {
  matchId: number;
  map: string;
  startedAt: string;
  endedAt: string | null;
  endReason: string | null;
  roundsPlayed: number;
  ctScore: number;
  tScore: number;
  teamAScore: number;
  teamBScore: number;
  modeFamily: "competitive" | "deathmatch";
  ruleset: string;
  gameType: number;
  gameMode: number;
  durationSeconds: number;
  status: string;
}

export interface Cs2ssRoundSummary {
  roundId: number;
  matchId: number;
  roundNumber: number;
  capturedAt: string;
  source: string;
  winnerTeam: string | null;
  endReason: number | null;
  ctScore: number;
  tScore: number;
  teamAScore: number;
  teamBScore: number;
}

export interface Cs2ssRoundPlayer {
  roundPlayerId: number;
  roundId: number;
  matchId: number;
  steamId: string;
  name: string;
  team: "CT" | "T";
  isBot: boolean;
  alive: boolean;
  health: number;
  kills: number;
  deaths: number;
  assists: number;
  damage: number;
  headshotKills: number;
  totalKills: number;
  totalDeaths: number;
  totalDamage: number;
  score: number;
  money: number;
  kast: boolean;
  survived: boolean;
  traded: boolean;
  tradeKills: number;
  eventKills: number;
  multikill: number;
  clutchAttempt: boolean;
  clutchWon: boolean;
  clutchSize: number;
  roundNumber: number;
}

export interface Cs2ssMatchPlayer {
  matchPlayerId: number;
  matchId: number;
  steamId: string;
  name: string;
  team: "CT" | "T";
  isBot: boolean;
  alive: boolean;
  health: number;
  totalKills: number;
  totalDeaths: number;
  totalAssists: number;
  totalDamage: number;
  totalHeadshotKills: number;
  score: number;
  money: number;
  kastRounds: number;
  tradeKills: number;
  multikill2: number;
  multikill3: number;
  multikill4: number;
  multikill5: number;
  clutchAttempts: number;
  clutchesWon: number;
  dmSpawnCount: number;
  dmCompletedLives: number;
  dmMaxKillStreak: number;
  dmAliveSeconds: number;
  dmLongestLifeSeconds: number;
  dmBurst5s2: number;
  dmBurst5s3: number;
  dmBurst5s4: number;
  dmBurst10s2: number;
  dmBurst10s3: number;
  dmBurst10s4: number;
}

export interface Cs2ssDeathmatchLife {
  lifeId: number;
  matchId: number;
  steamId: string;
  lifeIndex: number;
  spawnedAt: string;
  endedAt: string;
  endKind: string;
  durationSeconds: number;
  kills: number;
  damage: number;
}

export interface Cs2ssMatchDetailResponse {
  match: Cs2ssMatchSummary;
  rounds: Cs2ssRoundSummary[];
  roundPlayers: Cs2ssRoundPlayer[];
  matchPlayers: Cs2ssMatchPlayer[];
  deathmatchLives: Cs2ssDeathmatchLife[];
}

export interface Cs2ssPlayerMatchSummary {
  matchId: number;
  map: string;
  startedAt: string;
  roundsPlayed: number;
  ctScore: number;
  tScore: number;
  teamAScore: number;
  teamBScore: number;
  team: string;
  initialTeam: string;
  totalKills: number;
  totalDeaths: number;
  totalAssists: number;
  totalDamage: number;
  totalHeadshotKills: number;
  score: number;
  money: number;
  kastRounds: number;
  tradeKills: number;
  multikill2: number;
  multikill3: number;
  multikill4: number;
  multikill5: number;
  clutchAttempts: number;
  clutchesWon: number;
}

export interface Cs2ssMapStat {
  map: string;
  matches: number;
  kills: number;
  deaths: number;
  assists: number;
  damage: number;
  headshots: number;
  rounds: number;
  kastRounds: number;
  tradeKills: number;
  multikill2: number;
  multikill3: number;
  multikill4: number;
  multikill5: number;
  clutchAttempts: number;
  clutchesWon: number;
}

export interface Cs2ssPlayerTotal {
  kills: number;
  deaths: number;
  assists: number;
  damage: number;
  headshots: number;
  rounds: number;
  kastRounds: number;
  tradeKills: number;
  multikill2: number;
  multikill3: number;
  multikill4: number;
  multikill5: number;
  clutchAttempts: number;
  clutchesWon: number;
}

export interface Cs2ssPlayerDetailResponse {
  steamId: string;
  name: string;
  total: Cs2ssPlayerTotal;
  matches: Cs2ssPlayerMatchSummary[];
  mapStats: Cs2ssMapStat[];
}

export interface Cs2ssConfig {
  steamId: string;
}

export interface Cs2ssDmMapStat {
  map: string;
  sessions: number;
  avgKpm: number;
  avgDpm: number;
  avgKd: number;
  maxStreak: number;
}

export interface Cs2ssDmSessionPoint {
  matchId: number;
  map: string;
  ruleset: string;
  kills: number;
  deaths: number;
  damage: number;
  score: number;
  kpm: number;
  dpm: number;
  kd: number;
  headshotPct: number;
  streak: number;
  durationSeconds: number;
  startedAt: string;
}

export interface Cs2ssDmOverview {
  sessionCount: number;
  totalKills: number;
  totalDeaths: number;
  totalDamage: number;
  totalHeadshots: number;
  totalScore: number;
  totalSpawns: number;
  totalAliveSec: number;
  totalSessionSec: number;
  maxStreak: number;
  maxLongestLife: number;
  totalBurst5_2: number;
  totalBurst5_3: number;
  totalBurst5_4: number;
  totalBurst10_2: number;
  totalBurst10_3: number;
  totalBurst10_4: number;
  perMap: Cs2ssDmMapStat[];
  sessions: Cs2ssDmSessionPoint[];
}

export interface Cs2ssMatchWithStats extends Cs2ssMatchSummary {
  playerTeam: string;
  playerInitialTeam: string;
  playerKills: number;
  playerDeaths: number;
  playerAssists: number;
  playerDamage: number;
  playerHeadshots: number;
  playerScore: number;
  playerKastRounds: number;
  playerTradeKills: number;
  playerMk2: number;
  playerMk3: number;
  playerMk4: number;
  playerMk5: number;
  playerClutchAttempts: number;
  playerClutchesWon: number;
  playerDmSpawnCount: number;
  playerDmMaxKillStreak: number;
}

export interface Cs2ssPlayerMatchStats {
  matchId: number;
  map: string;
  startedAt: string;
  roundsPlayed: number;
  ctScore: number;
  tScore: number;
  team: string;
  kills: number;
  deaths: number;
  assists: number;
  damage: number;
  headshotKills: number;
  rating: number;
  adr: number;
  kd: number;
  kda: number;
  kpr: number;
  dpr: number;
  apr: number;
  hsPct: number;
  playerWins: number;
  opponentWins: number;
  won: boolean;
  modeFamily?: "competitive" | "deathmatch";
  score?: number;
  durationSeconds?: number;
}

export interface Cs2ssTacticalMatch {
  matchId: number;
  map: string;
  startedAt: string;
  roundsPlayed: number | null;
  aiTree: string | null;
  roundsWithPaths: number;
}

export interface Cs2ssTacticalRound {
  roundNumber: number;
  winnerTeam: string | null;
  teamAScore: number;
  teamBScore: number;
}

export interface Cs2ssTacticalPath {
  roundNumber: number;
  steamId: string;
  name: string;
  team: "CT" | "T";
  sampleHz: number;
  points: [number, number][];
}

export interface Cs2ssTacticalGrenade {
  roundNumber: number;
  steamId: string;
  name: string;
  team: "CT" | "T";
  kind: string;
  thrownSeconds: number;
  detonatedSeconds: number;
  from: [number, number, number];
  to: [number, number, number];
}

export interface Cs2ssTacticalDeath {
  roundNumber: number;
  steamId: string;
  name: string;
  team: "CT" | "T";
  atSeconds: number;
  position: [number, number];
}

export interface Cs2ssTacticalMatchData {
  matchId: number;
  map: string;
  startedAt: string;
  aiTree: string | null;
  rounds: Cs2ssTacticalRound[];
  paths: Cs2ssTacticalPath[];
  grenades: Cs2ssTacticalGrenade[];
  deaths: Cs2ssTacticalDeath[];
}

export interface Cs2ssTacticalAggregateMatch {
  matchId: number;
  startedAt: string;
  aiTree: string | null;
  rounds: number;
}

export interface Cs2ssTacticalAggregatePath {
  matchId: number;
  roundNumber: number;
  aiTree: string | null;
  winner: string | null;
  steamId: string;
  name: string;
  team: "CT" | "T";
  sampleHz: number;
  points: [number, number][];
}

export interface Cs2ssTacticalAggregate {
  map: string;
  matchLimit: number;
  matches: Cs2ssTacticalAggregateMatch[];
  paths: Cs2ssTacticalAggregatePath[];
}

export interface Cs2ssBotRole {
  name: string;
  role: string;
  weaponTemplate: string;
  skillTemplate: string;
  aggression: number;
  teamwork: number;
}

export interface Cs2ssTacticalAggregateMatch {
  matchId: number;
  startedAt: string;
  aiTree: string | null;
  rounds: number;
}

export interface Cs2ssTacticalAggregatePath {
  matchId: number;
  roundNumber: number;
  aiTree: string | null;
  winner: string | null;
  steamId: string;
  name: string;
  team: "CT" | "T";
  sampleHz: number;
  points: [number, number][];
}

export interface Cs2ssTacticalAggregate {
  map: string;
  matchLimit: number;
  matches: Cs2ssTacticalAggregateMatch[];
  paths: Cs2ssTacticalAggregatePath[];
}

export interface Cs2ssBotRole {
  name: string;
  role: string;
  weaponTemplate: string;
  skillTemplate: string;
  aggression: number;
  teamwork: number;
}