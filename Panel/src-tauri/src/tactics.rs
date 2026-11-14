use crate::{write_json_atomic, AppError, Result};
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;
use std::path::{Path, PathBuf};

// The PlusTactics plugin owns .csbip/tactics.json at runtime. The panel only
// edits that file and lists the tree files the engine can actually load, so the
// two never disagree about where a tactic lives.

const TREE_DIR: &str = "scripts/ai/localarena";

fn validated_csgo(raw: &str) -> Result<PathBuf> {
    let selected = PathBuf::from(raw.trim().trim_matches('"'));
    let candidates = [
        selected.clone(),
        selected.join("game").join("csgo"),
        selected.join("csgo"),
    ];
    fn looks_like_csgo(path: &Path) -> bool {
        path.join("gameinfo.gi").is_file() && path.join("cfg").is_dir()
    }
    for candidate in candidates {
        if looks_like_csgo(&candidate) {
            return std::fs::canonicalize(&candidate).map_err(|error| {
                AppError::directory(format!("Cannot resolve the selected CS2 directory: {error}"))
            });
        }
    }
    Err(AppError::directory(format!(
        "The selected path is not a CS2 game/csgo directory: {}",
        selected.display()
    )))
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TacticsState {
    #[serde(default = "default_enabled")]
    pub enabled: bool,
    #[serde(default = "default_mode")]
    pub mode: String,
    #[serde(default)]
    pub fixed_tree: Option<String>,
    #[serde(default)]
    pub pool: Vec<String>,
    /// Waypoint name -> [x, y, z]; the plugin writes an object, not a list.
    #[serde(default)]
    pub waypoints: BTreeMap<String, Vec<f32>>,
}

fn default_enabled() -> bool { true }
fn default_mode() -> String { "per_match".to_string() }

impl Default for TacticsState {
    fn default() -> Self {
        Self { enabled: true, mode: default_mode(), fixed_tree: None, pool: Vec::new(), waypoints: BTreeMap::new() }
    }
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TacticsTree {
    pub id: String,
    pub path: String,
    pub bytes: u64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TacticsActive {
    pub tree: Option<String>,
    pub path: Option<String>,
    pub map: Option<String>,
    pub mode: Option<String>,
    pub applied_at: Option<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TacticsSnapshot {
    pub state: TacticsState,
    pub trees: Vec<TacticsTree>,
    pub active: TacticsActive,
    pub config_path: String,
    pub tree_dir_exists: bool,
}

fn tactics_path(csgo: &Path) -> PathBuf {
    csgo.join(".csbip").join("tactics.json")
}

fn active_path(csgo: &Path) -> PathBuf {
    csgo.join(".csbip").join("active-tactics.json")
}

fn list_trees(csgo: &Path) -> Vec<TacticsTree> {
    let mut trees = Vec::new();
    let dir = csgo.join(TREE_DIR);
    let Ok(entries) = std::fs::read_dir(&dir) else { return trees };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.extension().and_then(|ext| ext.to_str()) != Some("kv3") { continue; }
        let Some(stem) = path.file_stem().and_then(|stem| stem.to_str()) else { continue };
        let bytes = entry.metadata().map(|meta| meta.len()).unwrap_or(0);
        trees.push(TacticsTree {
            id: stem.to_string(),
            path: format!("{TREE_DIR}/{stem}.kv3"),
            bytes,
        });
    }
    trees.sort_by(|left, right| left.id.cmp(&right.id));
    trees
}

fn read_active(csgo: &Path) -> TacticsActive {
    let empty = TacticsActive { tree: None, path: None, map: None, mode: None, applied_at: None };
    let Ok(text) = std::fs::read_to_string(active_path(csgo)) else { return empty };
    let Ok(value) = serde_json::from_str::<serde_json::Value>(&text) else { return empty };
    let string = |key: &str| value.get(key).and_then(|item| item.as_str()).map(|item| item.to_string());
    TacticsActive {
        tree: string("tree"),
        path: string("path"),
        map: string("map"),
        mode: string("mode"),
        applied_at: string("applied_at"),
    }
}

#[tauri::command]
pub fn get_tactics_snapshot(csgo: String) -> Result<TacticsSnapshot> {
    let root = validated_csgo(&csgo)?;
    let path = tactics_path(&root);
    let state = match std::fs::read_to_string(&path) {
        Ok(text) => serde_json::from_str::<TacticsState>(&text).unwrap_or_default(),
        Err(_) => TacticsState::default(),
    };
    Ok(TacticsSnapshot {
        state,
        trees: list_trees(&root),
        active: read_active(&root),
        config_path: path.display().to_string(),
        tree_dir_exists: root.join(TREE_DIR).is_dir(),
    })
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LiveMatch {
    pub match_id: i64,
    pub map: String,
    pub started_at: String,
    pub rounds_played: i64,
    pub ct_score: i64,
    pub t_score: i64,
    pub team_a_score: i64,
    pub team_b_score: i64,
    pub ai_tree: Option<String>,
}

/// The match the telemetry plugin is currently recording, if any. The match mode
/// screen polls this while the game runs; it is read-only and returns None when
/// the database or the table is missing.
#[tauri::command]
pub fn get_live_match(csgo: String) -> Result<Option<LiveMatch>> {
    let root = validated_csgo(&csgo)?;
    let db = root.join(".csbip").join("cs2ss").join("telemetry.db");
    if !db.is_file() { return Ok(None); }
    let conn = rusqlite::Connection::open_with_flags(
        &db,
        rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY | rusqlite::OpenFlags::SQLITE_OPEN_NO_MUTEX,
    )
    .map_err(|error| AppError::invalid(format!("Cannot open telemetry.db: {error}")))?;

    let row = conn.query_row(
        "SELECT match_id, map, started_at, COALESCE(rounds_played, 0), ct_score, t_score,
                COALESCE(team_a_score, 0), COALESCE(team_b_score, 0), ai_tree
         FROM matches WHERE status = 'in_progress' ORDER BY match_id DESC LIMIT 1",
        [],
        |row| {
            Ok(LiveMatch {
                match_id: row.get(0)?,
                map: row.get(1)?,
                started_at: row.get(2)?,
                rounds_played: row.get(3)?,
                ct_score: row.get(4)?,
                t_score: row.get(5)?,
                team_a_score: row.get(6)?,
                team_b_score: row.get(7)?,
                ai_tree: row.get(8)?,
            })
        },
    );

    match row {
        Ok(live) => Ok(Some(live)),
        Err(rusqlite::Error::QueryReturnedNoRows) => Ok(None),
        Err(error) => Err(AppError::invalid(format!("Live match query failed: {error}"))),
    }
}

#[tauri::command]
pub fn save_tactics_state(csgo: String, state: TacticsState) -> Result<()> {
    let root = validated_csgo(&csgo)?;
    let mut state = state;
    if !matches!(state.mode.as_str(), "per_match" | "per_half" | "fixed") {
        return Err(AppError::invalid(format!("Unknown tactics mode: {}", state.mode)));
    }
    let available = list_trees(&root);
    let known: Vec<&str> = available.iter().map(|tree| tree.id.as_str()).collect();
    for id in state.pool.iter().chain(state.fixed_tree.iter()) {
        if !known.contains(&id.as_str()) {
            return Err(AppError::invalid(format!("Unknown tactic tree: {id}")));
        }
    }
    state.pool.retain(|id| known.contains(&id.as_str()));
    state.pool.sort();
    state.pool.dedup();
    let path = tactics_path(&root);
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent).map_err(AppError::transaction_io)?;
    }
    write_json_atomic(&path, &state)
}
