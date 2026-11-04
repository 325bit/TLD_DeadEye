#!/usr/bin/env python3
"""Build TLD_DeadEye and package it as a GitHub-release zip.

The zip is what a player downloads, so it is laid out exactly like a game
folder: the "Mods" tree inside it can be merged into the game root as-is.

    TLD_DeadEye_v<version>.zip
    +-- TLD_DeadEye_v<version>/
        +-- Mods/
        |   +-- TLD_DeadEye.dll
        |   +-- DeadEye/Asset/*.wav
        +-- README.md
        +-- LICENSE

Sources of truth read at run time (so nothing here drifts):
  BuildInfo.cs        -> version / mod name
  TLD_DeadEye.csproj  -> assembly name, asset folder name, live-install path
  Asset/*.wav         -> the three shipped sound files (mp3 in that folder are
                         editable sources, see docs/DEVELOPMENT.md 15)

Usage:
  python tools/pack_release.py                 # build Release, then pack
  python tools/pack_release.py -n              # pack what bin/ already holds
  python tools/pack_release.py -c Debug        # tuning build (not for release)
  python tools/pack_release.py --strict        # fail on a version mismatch

Exit code 0 = zip written, 1 = nothing usable was produced.
"""

import argparse
import hashlib
import re
import shutil
import subprocess
import sys
import time
import zipfile
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_ROOT = SCRIPT_DIR.parent

# Zip entry timestamps must be >= 1980 for the zip format.
MIN_ZIP_YEAR = 1980


def note(msg):
    print(msg)


def warn(msg):
    print("  [warn] " + msg)


def read_text(path):
    return path.read_text(encoding="utf-8-sig", errors="replace")


def find_one(paths, what):
    paths = list(paths)
    if not paths:
        raise SystemExit("  [error] %s not found" % what)
    if len(paths) > 1:
        raise SystemExit("  [error] more than one %s: %s" % (what, ", ".join(p.name for p in paths)))
    return paths[0]


# ---------------------------------------------------------------- metadata


def read_metadata(root):
    """Pull the version, assembly name and asset folder out of the sources."""
    build_info = root / "BuildInfo.cs"
    if not build_info.is_file():
        raise SystemExit("  [error] BuildInfo.cs not found in %s" % root)
    csproj = find_one(root.glob("*.csproj"), "csproj")
    bi_text = read_text(build_info)
    cs_text = read_text(csproj)

    def grab(pattern, text, label, default=None):
        m = re.search(pattern, text)
        if m:
            return m.group(1).strip()
        if default is not None:
            return default
        raise SystemExit("  [error] cannot read %s (%s)" % (label, pattern))

    meta = {
        "root": root,
        "csproj": csproj,
        "version": grab(r'Version\s*=\s*"([^"]+)"', bi_text, "version from BuildInfo.cs"),
        "mod_name": grab(r'\bName\s*=\s*"([^"]+)"', bi_text, "mod name from BuildInfo.cs"),
        "gui_name": grab(r'GUIName\s*=\s*"([^"]+)"', bi_text, "GUI name from BuildInfo.cs", default=""),
        "assembly": grab(r"<AssemblyName>\s*([^<]+?)\s*</AssemblyName>", cs_text,
                         "AssemblyName from the csproj", default=csproj.stem),
        "game_dir": grab(r"<TheLongDarkPath>\s*([^<]+?)\s*</TheLongDarkPath>", cs_text,
                         "TheLongDarkPath from the csproj", default=""),
        # The csproj copies the wavs to $(ReleaseFolderPath)/<folder>/Asset; the
        # mod resolves them at "<dll folder>/<folder>/Asset" (Core/ModAudio.cs).
        "asset_folder": grab(r'DestinationFolder="\$\(ReleaseFolderPath\)/([^/"]+)/Asset"',
                             cs_text, "asset folder name from the csproj", default="DeadEye"),
    }
    return meta


def version_mismatches(meta):
    """Every place a version number is written down (docs/DEVELOPMENT.md 16)."""
    root, version = meta["root"], meta["version"]
    found = []

    readme = root / "README.md"
    if readme.is_file():
        m = re.search(r"badge/version-([0-9][0-9A-Za-z.\-]*)-", read_text(readme))
        found.append(("README.md badge", m.group(1) if m else None))
    else:
        found.append(("README.md badge", None))

    changelog = root / "ChangeLog.md"
    if changelog.is_file():
        m = re.search(r"^##\s+(\S+)\s*$", read_text(changelog), re.MULTILINE)
        found.append(("ChangeLog.md first section", m.group(1) if m else None))
    else:
        found.append(("ChangeLog.md first section", None))

    if not meta["assembly"] == meta["mod_name"]:
        found.append(("csproj AssemblyName", meta["assembly"]))

    return [(label, value) for label, value in found if value != version]


def package_name(meta, configuration):
    """Base name of both the zip and the folder inside it.

    A Debug build gets its own name so packing one can never overwrite the
    release zip of the same version.
    """
    return "TLD_DeadEye_v" + meta["version"] + ("-debug" if configuration == "Debug" else "")


# ---------------------------------------------------------------- build


def locate_dotnet():
    exe = shutil.which("dotnet")
    if not exe:
        raise SystemExit("  [error] dotnet not on PATH; install the .NET SDK or pass -n (skip build)")
    return exe


def build(meta, configuration):
    cmd = [locate_dotnet(), "build", str(meta["csproj"]),
           "-c", configuration, "--nologo", "-v", "minimal"]
    note("  $ " + " ".join('"%s"' % c if " " in c else c for c in cmd))
    result = subprocess.run(cmd, cwd=str(meta["root"]))
    if result.returncode != 0:
        raise SystemExit("  [error] dotnet build failed (exit %d)" % result.returncode)


def locate_dll(meta, configuration):
    """The freshest <AssemblyName>.dll under bin/<Configuration>/ (any TFM)."""
    bin_dir = meta["root"] / "bin" / configuration
    if not bin_dir.is_dir():
        raise SystemExit("  [error] %s does not exist; build %s first (or drop -n)"
                         % (bin_dir, configuration))
    wanted = meta["assembly"] + ".dll"
    candidates = [p for p in bin_dir.rglob(wanted) if p.parent.name.lower() != "ref"]
    if not candidates:
        raise SystemExit("  [error] %s not found under %s" % (wanted, bin_dir))
    dll = max(candidates, key=lambda p: p.stat().st_mtime)

    build_info = meta["root"] / "BuildInfo.cs"
    if build_info.stat().st_mtime > dll.stat().st_mtime:
        warn("BuildInfo.cs is newer than the dll - the version in the zip may be stale")
    if dll.stat().st_size == 0:
        raise SystemExit("  [error] %s is empty" % dll)
    return dll


def locate_assets(meta):
    """Shipped wavs: Asset/*.wav, falling back to the post-build copy."""
    primary = meta["root"] / "Asset"
    fallback = meta["root"] / "Release" / meta["asset_folder"] / "Asset"
    for folder, is_primary in ((primary, True), (fallback, False)):
        if folder.is_dir():
            wavs = sorted(folder.glob("*.wav"))
            if wavs:
                if not is_primary:
                    warn("Asset/*.wav missing, using the post-build copy in %s" % folder)
                return wavs
    raise SystemExit("  [error] no *.wav found in Asset/ or Release/%s/Asset/"
                     % meta["asset_folder"])


# ---------------------------------------------------------------- staging


def stage(meta, name, dll, wavs, include_docs):
    """Build the folder tree that becomes the zip, and return its path."""
    staging_root = meta["root"] / "obj" / "pack" / name
    if staging_root.exists():
        shutil.rmtree(staging_root)

    mods = staging_root / "Mods"
    assets = mods / meta["asset_folder"] / "Asset"
    assets.mkdir(parents=True)
    shutil.copy2(dll, mods / dll.name)
    for wav in wavs:
        shutil.copy2(wav, assets / wav.name)

    if include_docs:
        for doc in ("README.md", "LICENSE"):
            src = meta["root"] / doc
            if src.is_file():
                shutil.copy2(src, staging_root / doc)
            else:
                warn("%s not found, left out of the zip" % doc)

    return staging_root


def write_zip(staging_root, zip_path):
    """Zip with forward-slash entries (Compress-Archive 5.1 writes backslashes)."""
    if zip_path.exists():
        zip_path.unlink()
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    files = sorted(p for p in staging_root.rglob("*") if p.is_file())
    entries = []
    with zipfile.ZipFile(str(zip_path), "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for path in files:
            arcname = path.relative_to(staging_root.parent).as_posix()
            info = zipfile.ZipInfo(arcname, date_time=_zip_time(path))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            zf.writestr(info, path.read_bytes())
            entries.append((arcname, path.stat().st_size))
    return entries


def _zip_time(path):
    stamp = time.localtime(path.stat().st_mtime)[:6]
    return stamp if stamp[0] >= MIN_ZIP_YEAR else (MIN_ZIP_YEAR, 1, 1, 0, 0, 0)


# ---------------------------------------------------------------- reporting


def git_facts(root):
    def run(*args):
        try:
            out = subprocess.run(["git", "-C", str(root)] + list(args),
                                 capture_output=True, text=True)
            return out.stdout.strip() if out.returncode == 0 else None
        except FileNotFoundError:
            return None

    commit = run("rev-parse", "--short", "HEAD")
    dirty = run("status", "--porcelain")
    return commit, bool(dirty)


def human(size):
    for unit in ("B", "KB", "MB"):
        if size < 1024 or unit == "MB":
            return ("%d %s" % (size, unit)) if unit == "B" else ("%.1f %s" % (size, unit))
        size /= 1024.0


def print_tree(entries):
    """Print the zip layout as a tree, so the Mods/ subtree is unmistakable."""
    tree = {}
    for arcname, size in entries:
        parts = arcname.split("/")
        node = tree
        for part in parts[:-1]:
            node = node.setdefault(part + "/", {})
        node[parts[-1]] = size

    def walk(node, indent):
        for name in sorted(k for k in node if k.endswith("/")):
            note("    " + "  " * indent + name)
            walk(node[name], indent + 1)
        for name in sorted(k for k in node if not k.endswith("/")):
            note("    " + ("  " * indent + name).ljust(38) + human(node[name]))

    walk(tree, 1)


def report(meta, zip_path, entries, configuration):
    root = meta["root"]
    digest = hashlib.sha256(zip_path.read_bytes()).hexdigest()
    commit, dirty = git_facts(root)
    try:
        shown = zip_path.relative_to(root).as_posix()
    except ValueError:
        shown = str(zip_path)

    note("")
    note("  Contents of %s:" % zip_path.name)
    print_tree(entries)

    note("")
    note("  Zip        : %s" % shown)
    note("  Size       : %s" % human(zip_path.stat().st_size))
    note("  SHA-256    : %s" % digest)
    note("  Build      : %s%s" % (configuration,
                                 "" if configuration == "Release" else "  (NOT for release)"))
    note("  Source     : %s" % (commit or "unknown commit"))
    if dirty:
        warn("the working tree has uncommitted changes - this zip was not built from a commit")
    if meta["game_dir"]:
        note("  Live mods  : %s\\Mods" % meta["game_dir"])
    note("")
    note("  Player install: extract the zip, then move its 'Mods' folder into the game")
    note("  root (next to TheLongDark.exe). ModSettings must already be installed.")
    note("")
    note("  Upload it with:")
    note('    gh release create v%s "%s" --title "v%s" --generate-notes'
         % (meta["version"], shown, meta["version"]))
    note("  or attach the file by hand on the GitHub release page.")


# ---------------------------------------------------------------- main


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Build TLD_DeadEye and package it as a GitHub-release zip.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="The zip holds a 'Mods' tree plus README/LICENSE, ready to merge into the game root.")
    parser.add_argument("-c", "--configuration", default="Release",
                        choices=["Release", "Debug"], help="build configuration (default: Release)")
    parser.add_argument("-n", "--skip-build", action="store_true",
                        help="pack the dll already in bin/<configuration>/")
    parser.add_argument("-o", "--out", default=None,
                        help="output folder for the zip (default: <repo>/dist)")
    parser.add_argument("--no-docs", action="store_true",
                        help="leave README.md and LICENSE out of the zip")
    parser.add_argument("--strict", action="store_true",
                        help="abort when a version number disagrees with BuildInfo.cs")
    parser.add_argument("--keep-staging", action="store_true",
                        help="keep obj/pack/<name>/ for inspection")
    parser.add_argument("--root", default=str(DEFAULT_ROOT),
                        help="repo root (default: the parent of tools/)")
    args = parser.parse_args(argv)

    root = Path(args.root).resolve()
    if not root.is_dir():
        raise SystemExit("  [error] repo root not found: %s" % root)

    note("== TLD_DeadEye release packaging ==")
    meta = read_metadata(root)
    note("  Mod      : %s (%s)" % (meta["mod_name"], meta["gui_name"]))
    note("  Version  : %s" % meta["version"])
    note("  Assembly : %s.dll" % meta["assembly"])
    if args.configuration == "Debug":
        note("")
        note("  !!! DEBUG BUILD: tuning sliders are compiled in - not for a stable release.")
        note("  !!! Written as a separate -debug zip, the release zip is left alone.")

    drift = version_mismatches(meta)
    if drift:
        for label, value in drift:
            warn("%s says %s, BuildInfo.cs says %s" % (label, value, meta["version"]))
        if args.strict:
            raise SystemExit("  [error] --strict: fix the version numbers before releasing")
    else:
        note("  Versions : consistent (BuildInfo.cs, README badge, ChangeLog.md)")

    if args.skip_build:
        note("")
        note("  Skipping the build (-n)")
    else:
        note("")
        note("  Building %s ..." % args.configuration)
        build(meta, args.configuration)

    dll = locate_dll(meta, args.configuration)
    wavs = locate_assets(meta)
    note("")
    note("  dll      : %s (%s)" % (dll.relative_to(root).as_posix(), human(dll.stat().st_size)))
    note("  audio    : %s" % ", ".join(w.name for w in wavs))

    staged = stage(meta, package_name(meta, args.configuration), dll, wavs,
                   include_docs=not args.no_docs)
    out_dir = Path(args.out).resolve() if args.out else root / "dist"
    zip_path = out_dir / (staged.name + ".zip")
    entries = write_zip(staged, zip_path)

    report(meta, zip_path, entries, args.configuration)

    if not args.keep_staging:
        shutil.rmtree(staged, ignore_errors=True)
    else:
        note("  Staging kept: %s" % staged)
    return 0


if __name__ == "__main__":
    sys.exit(main())
