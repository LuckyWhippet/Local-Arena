import { invoke } from "@tauri-apps/api/core";

// Kept apart from lib/api.ts so the tactics surface can evolve without touching
// the shared command table.
export interface TacticsState {
  enabled: boolean;
  mode: "per_match" | "per_half" | "fixed";
  fixedTree: string | null;
  pool: string[];
  waypoints: Record<string, number[]>;
}

export interface TacticsTree {
  id: string;
  path: string;
  bytes: number;
}

export interface TacticsActive {
  tree: string | null;
  path: string | null;
  map: string | null;
  mode: string | null;
  appliedAt: string | null;
}

export interface TacticsSnapshot {
  state: TacticsState;
  trees: TacticsTree[];
  active: TacticsActive;
  configPath: string;
  treeDirExists: boolean;
}

export const tacticsApi = {
  snapshot: (csgo: string) => invoke<TacticsSnapshot>("get_tactics_snapshot", { csgo }),
  save: (csgo: string, state: TacticsState) => invoke<void>("save_tactics_state", { csgo, state }),
};
