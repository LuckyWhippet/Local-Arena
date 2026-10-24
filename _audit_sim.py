def game_path_value(line):
    content = line.split("//", 1)[0] if "//" in line else line
    fields = content.split()
    if not fields or fields[0].lower() != "game":
        return None
    if len(fields) < 2:
        return None
    if len(fields) > 2:
        return None
    return fields[1]


def rewrite(text, include_bot_runtime):
    # mirrors mode_files.rs:57 -- literal backslash-r backslash-n
    newline = "\\r\\n" if "\\r\\n" in text else "\n"
    trailing_newline = text.endswith("\n")
    output = []
    inserted = False
    # Rust str::lines(): split on \n, strip trailing \r
    for line in text.split("\n"):
        if line.endswith("\r"):
            line = line[:-1]
        v = game_path_value(line)
        if v is not None and v.lower() in ("csgo/addons/metamod", "csgo/overrides/botprofile.vpk"):
            continue
        if include_bot_runtime and not inserted:
            v2 = game_path_value(line)
            if v2 is not None and v2.lower() == "csgo":
                indent = "".join(ch for ch in line[:len(line) - len(line.lstrip())])
                output.append(f"{indent}Game\tcsgo/addons/metamod")
                output.append(f"{indent}Game\tcsgo/overrides/botprofile.vpk")
                inserted = True
        output.append(line)
    if include_bot_runtime and not inserted:
        raise RuntimeError("gameinfo.gi has no primary 'Game csgo' SearchPath")
    rewritten = newline.join(output)
    if trailing_newline:
        rewritten += newline
    return rewritten


# --- Test A: the actual test fixture bytes from mode_files.rs:187 ---
# b"SearchPaths\\r\\n{\\r\\n\\tGame\\tcsgo\\r\\n}\\r\\nNewDepotSetting\\t1\\r\\n"
fixture = "SearchPaths\\r\\n{\\r\\n\tGame\tcsgo\\r\\n}\\r\\nNewDepotSetting\t1\\r\\n"
print("TEST A (test fixture, literal \\r\\n):")
print("  repr:", repr(fixture))
print("  lines() count:", len(fixture.split("\n")))
try:
    out = rewrite(fixture, False)
    print("  online ok:", repr(out))
    out2 = rewrite(out, True)
    print("  bots   ok:", repr(out2))
except RuntimeError as e:
    print("  RAISED:", e)

# --- Test B: a REAL CRLF gameinfo ---
real_crlf = "SearchPaths\r\n{\r\n\tGame\tcsgo\r\n}\r\nNewDepotSetting\t1\r\n"
print("\nTEST B (real CRLF file):")
print("  detected newline is CRLF?", "\\r\\n" in real_crlf)
out = rewrite(real_crlf, False)
print("  output repr:", repr(out))
print("  still CRLF?", "\r\n" in out, "| became LF-only?", "\r\n" not in out and "\n" in out)
out2 = rewrite(out, True)
print("  bots output repr:", repr(out2))

# --- Test C: idempotency over repeated switches ---
print("\nTEST C (idempotency, real LF file):")
cur = "SearchPaths\n{\n\tGame csgo/addons/metamod\n\tGame csgo/overrides/botprofile.vpk\n\tGame csgo\n}\n"
seen = []
for i in range(4):
    cur = rewrite(cur, True) if i % 2 == 0 else rewrite(cur, False)
    seen.append(cur)
print("  online->bots->online->bots stable:",
      seen[1] == seen[3])
for s in seen:
    print("   ", repr(s))
