import ancient from "../assets/radars/de_ancient.png";
import anubis from "../assets/radars/de_anubis.png";
import dust2 from "../assets/radars/de_dust2.png";
import inferno from "../assets/radars/de_inferno.png";
import mirage from "../assets/radars/de_mirage.png";
import nuke from "../assets/radars/de_nuke.png";
import overpass from "../assets/radars/de_overpass.png";
import train from "../assets/radars/de_train.png";
import vertigo from "../assets/radars/de_vertigo.png";

export interface RadarInfo {
  image: string;
  posX: number;
  posY: number;
  scale: number;
}

// pos_x / pos_y / scale come from the map overview calibration the game ships.
// Radar pixels: px = (x - pos_x) / scale, py = (pos_y - y) / scale, on a 1024 image.
const RADARS: Record<string, RadarInfo> = {
  de_ancient: { image: ancient, posX: -2953, posY: 2164, scale: 5.0 },
  de_anubis: { image: anubis, posX: -2796, posY: 3328, scale: 5.22 },
  de_dust2: { image: dust2, posX: -2476, posY: 3239, scale: 4.4 },
  de_inferno: { image: inferno, posX: -2087, posY: 3870, scale: 4.9 },
  de_mirage: { image: mirage, posX: -3230, posY: 1713, scale: 5.0 },
  de_nuke: { image: nuke, posX: -3453, posY: 2887, scale: 7.0 },
  de_overpass: { image: overpass, posX: -4831, posY: 1781, scale: 5.2 },
  de_train: { image: train, posX: -2308, posY: 2078, scale: 4.082077 },
  de_vertigo: { image: vertigo, posX: -3168, posY: 1762, scale: 4.0 },
};

export const RADAR_SIZE = 1024;

export function radarFor(map: string): RadarInfo | null {
  return RADARS[map] ?? null;
}

export function radarPoint(map: string, x: number, y: number): [number, number] | null {
  const radar = radarFor(map);
  if (!radar) return null;
  return [(x - radar.posX) / radar.scale, (radar.posY - y) / radar.scale];
}
