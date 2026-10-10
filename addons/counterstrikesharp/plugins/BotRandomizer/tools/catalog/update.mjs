import { execFileSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// npm --prefix tools/catalog run update (latest), or run generate (locked data only).
const root = new URL("./", import.meta.url);
const catalogPath = new URL("../../cosmetic_catalog.json", root);
const pluginPath = new URL("../../BotRandomizer.cs", root);
const read = path => JSON.parse(readFileSync(new URL(path, root), "utf8"));
const previous = read("../../cosmetic_catalog.json");
if (!Array.isArray(previous.knifeFinishPreferences) || !previous.knifeFinishPreferences.length) {
  throw new Error("The existing catalog must contain knife finish preferences");
}
const generateOnly = process.argv.includes("--generate");
let source = previous.source;
let plugin;
if (!generateOnly) {
  const npmPath = process.env.npm_execpath;
  if (!npmPath) throw new Error("Run: npm --prefix tools/catalog run update");
  const npm = (...args) => execFileSync(process.execPath, [npmPath, ...args], {
    cwd: fileURLToPath(root), encoding: "utf8", stdio: ["ignore", "pipe", "inherit"],
  });
  plugin = readFileSync(pluginPath, "utf8");
  // Sync the two functions used here; keep the author's inline C# signatures.
  const signatureUrl = "https://raw.githubusercontent.com/ianlucas/cs2-css-inventory-simulator/main/gamedata/inventory-simulator.json";
  const response = await fetch(signatureUrl, { signal: AbortSignal.timeout(30_000) });
  if (!response.ok) throw new Error(`Signature download failed: HTTP ${response.status}`);
  const gamedata = await response.json();
  for (const [marker, name] of [
    ["SetOrAddAttributeValueByName", "CAttributeList::SetOrAddAttributeValueByName"],
    ["CEconItemViewConstructor", "CEconItemView::CEconItemView"],
  ]) {
    const signatures = gamedata[name]?.signatures;
    if (signatures?.library !== "server") throw new Error(`Missing server signatures: ${name}`);
    const values = ["linux", "windows"].map(platform => {
      const value = signatures[platform];
      if (typeof value !== "string" || !/^(?:[\da-f]{2}|\?{1,2})(?: (?:[\da-f]{2}|\?{1,2})){3,}$/i.test(value)
          || !/[\da-f]{2}/i.test(value)) {
        throw new Error(`Invalid ${platform} signature: ${name}`);
      }
      return value.toUpperCase().replace(/\?\?/g, "?");
    });
    const pattern = new RegExp(
      `(// ${marker}\\r?\\n\\s*\\w+ = new MemoryFunctionWithReturn<[^;]+?` +
      `RuntimeInformation\\.IsOSPlatform\\(OSPlatform\\.Linux\\)\\s*\\? ")[^"]+` +
      `("\\s*:\\s*")[^"]+("\\);)`, "g");
    if ([...plugin.matchAll(pattern)].length !== 1) {
      throw new Error(`Expected exactly one inline signature block for ${marker}; source was not changed`);
    }
    plugin = plugin.replace(pattern, (_, prefix, middle, suffix) =>
      `${prefix}${values[0]}${middle}${values[1]}${suffix}`);
  }
  const metadata = JSON.parse(npm("view", "@ianlucas/cs2-lib@latest", "version", "gitHead", "--json"));
  if (!/^\d+\.\d+\.\d+$/.test(metadata.version) || !/^[a-f0-9]{40}$/.test(metadata.gitHead)) {
    throw new Error("Expected a stable cs2-lib release with a source commit");
  }
  console.log(npm("install", "--save-exact", "--ignore-scripts", "--no-audit", "--no-fund",
    `@ianlucas/cs2-lib@${metadata.version}`));
  source = { package: "@ianlucas/cs2-lib", version: metadata.version, commit: metadata.gitHead };
}

const manifest = read("package.json");
const installed = read("node_modules/@ianlucas/cs2-lib/package.json");
const lock = read("package-lock.json");
const entry = lock.packages?.["node_modules/@ianlucas/cs2-lib"];
if (source?.package !== "@ianlucas/cs2-lib" || !/^\d+\.\d+\.\d+$/.test(source.version) ||
    !/^[a-f0-9]{40}$/.test(source.commit) || manifest.dependencies[source.package] !== source.version ||
    installed.name !== source.package || installed.version !== source.version ||
    entry?.version !== source.version || !entry.integrity ||
    lock.packages?.[""].dependencies?.[source.package] !== source.version ||
    (generateOnly && source.integrity !== entry.integrity)) {
  throw new Error("Catalog source, dependency, lockfile and installed cs2-lib must agree");
}
const integrity = entry.integrity;
// Import after npm install so this run uses the updated package.
const { CS2_ITEMS, CS2RarityColorOrder } = await import("@ianlucas/cs2-lib");
const { english } = await import("@ianlucas/cs2-lib/translations/english");

const rarities = [null, "consumer", "industrial", "milSpec", "restricted", "classified", "covert", "contraband"];
const positive = (value, label) => {
  if (!Number.isSafeInteger(value) || value <= 0) throw new Error(`Invalid ${label}: ${value}`);
  return value;
};
const variants = (type) => CS2_ITEMS.filter(item => item.type === type && item.variantIndex > 0);
const unique = (values) => [...new Set(values)].sort((a, b) => a - b);
const wear = (item) => {
  const wearMin = item.wearMin;
  const wearMax = item.wearMax;
  if (!Number.isFinite(wearMin) || !Number.isFinite(wearMax) ||
      wearMin < 0 || wearMax > 1 || wearMin > wearMax) {
    throw new Error(`Invalid wear on item ${item.id}`);
  }
  return { wearMin, wearMax };
};
const paintKit = (item) => positive(item.variantIndex, `paint on ${item.id}`);
const definition = (item) => positive(item.definitionIndex, `definition on ${item.id}`);
const byPaint = (a, b) => a.paintKit - b.paintKit;
const group = (items) => unique(items.map(definition)).map(defIndex => ({
  defIndex, items: items.filter(item => item.definitionIndex === defIndex),
}));
const weapons = group(variants("weapon")).map(({ defIndex, items }) => {
  const base = CS2_ITEMS.find(item => item.type === "weapon" && item.isBase && item.definitionIndex === defIndex);
  if (!base?.modelKey) throw new Error(`Missing weapon base ${defIndex}`);
  return {
    designerName: `weapon_${base.modelKey}`, defIndex,
    stickerSchemaCount: positive(base.stickerSchemaCount, `sticker schemas on ${defIndex}`),
    legacyStickerSchemaCount: positive(base.legacyStickerSchemaCount ?? base.stickerSchemaCount, `legacy schemas on ${defIndex}`),
    paints: items.map(item => {
      const rarity = rarities[CS2RarityColorOrder[item.rarityColor]];
      if (!rarity) throw new Error(`Unknown rarity on item ${item.id}`);
      return { paintKit: paintKit(item), rarity, legacy: item.isLegacyModel === true, ...wear(item) };
    }).sort(byPaint),
  };
});
const knives = group(variants("melee")).map(({ defIndex, items }) => ({
  defIndex,
  paints: items.map(item => {
    const finish = english[item.id]?.name?.split(" | ")[1];
    if (!finish) throw new Error(`Missing knife finish on item ${item.id}`);
    return { paintKit: paintKit(item), finish, ...wear(item) };
  }).sort(byPaint),
}));
const gloves = variants("glove").map(item => ({
  defIndex: definition(item), paintKit: paintKit(item), ...wear(item),
})).sort((a, b) => a.defIndex - b.defIndex || byPaint(a, b));
const stickers = variants("sticker");
const stickerCategories = [...new Set(stickers.map(item => {
  const category = english[item.id]?.categoryName;
  if (!category) throw new Error(`Missing sticker category on item ${item.id}`);
  return category;
}))].sort((a, b) => {
  const left = a.toLowerCase();
  const right = b.toLowerCase();
  return left < right ? -1 : left > right ? 1 : a < b ? -1 : a > b ? 1 : 0;
});
const stickerKits = stickers.map(item => {
  const translation = english[item.id];
  if (!translation.name) throw new Error(`Missing sticker name on item ${item.id}`);
  const finish = /\((Glitter|Holo|Foil|Gold|Lenticular)(?:,|\))/.exec(translation.name)?.[1].toLowerCase() ?? "paper";
  return { defIndex: paintKit(item), finish, category: stickerCategories.indexOf(translation.categoryName) };
}).sort((a, b) => a.defIndex - b.defIndex);
const catalog = {
  source: { repository: "ianlucas/cs2-lib", commit: source.commit,
    package: source.package, version: source.version, integrity },
  weapons, knives, knifeFinishPreferences: previous.knifeFinishPreferences, gloves,
  stickerCategories, stickerKits,
  keychainDefinitions: unique(variants("keychain").map(paintKit)),
  musicKits: unique(variants("musickit").map(paintKit)).filter(id => id !== 1 && id !== 2),
};

// Write runtime files only after both signatures and the complete catalog are ready.
writeFileSync(catalogPath, JSON.stringify(catalog, null, 2) + "\n");
if (!generateOnly) writeFileSync(pluginPath, plugin);
console.log(`Updated ${generateOnly ? "catalog" : "signatures and catalog"}: cs2-lib ${source.version}, ${weapons.length} weapons, ${stickerKits.length} stickers.`);
