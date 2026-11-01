"""Validate prepared candidate archives without executing any installer (Python 3.11+)."""
import hashlib
import json
import sys
import zipfile
from pathlib import Path, PurePosixPath

root = Path(__file__).resolve().parents[2]
version = sys.argv[1] if len(sys.argv) > 1 else "0.6.25"
plan = json.loads((root / "artifacts" / f"release-{version}.json").read_text(encoding="utf-8-sig"))
assert plan["version"] == version and plan["productId"] == "unishare"
assert plan["prepared"] and not plan["performed"] and plan["nativeInstallersExecuted"] == 0
assert len(plan["artifacts"]) == 10
checks = []
for item in plan["artifacts"]:
    file = Path(item["path"])
    assert file.parent.resolve() == (root / "artifacts").resolve()
    assert item["version"] == version and file.stat().st_size == item["bytes"]
    with file.open("rb") as stream:
        assert hashlib.file_digest(stream, "sha256").hexdigest().upper() == item["sha256"]
    if file.suffix in (".apk", ".zip"):
        with zipfile.ZipFile(file) as archive:
            names = archive.namelist()
            assert len(names) == len(set(names)), f"Duplicate names: {file.name}"
            for name in names:
                normalized = name.replace("\\", "/")
                assert not PurePosixPath(normalized).is_absolute() and ".." not in PurePosixPath(normalized).parts and ":" not in normalized
            assert archive.testzip() is None, f"CRC mismatch: {file.name}"
    checks.append(f"{file.name}: size/SHA-256/archive paths/CRC")

def read(package, entry):
    with zipfile.ZipFile(root / "artifacts" / package) as archive:
        return archive.read(entry)

apk = f"UniShare-Android-{version}.apk"
assert read(f"UniShare-Android-{version}.zip", apk) == (root / "artifacts" / apk).read_bytes()
channel = json.loads((root / "shared/updates/channel.json").read_text())
assert json.loads(read(apk, "assets/updates/channel.json")) == channel
assert json.loads(read(f"UniShare-Windows-{version}.zip", "updates/channel.json")) == channel
for browser in ("Edge", "Chrome"):
    store = f"UniShare-{browser}-Store-{version}.zip"
    assert read(f"UniShare-{browser}-Submission-{version}.zip", store) == (root / "artifacts" / store).read_bytes()
    distribution = json.loads(read(store, "distribution.json"))
    assert distribution["store"] == browser.lower() and distribution["productId"] == "unishare"
    manifest = json.loads(read(store, "manifest.json"))
    assert manifest["version"] == version and "key" not in manifest and "update_url" not in manifest
    assert sorted(manifest["permissions"]) == ["activeTab", "scripting", "storage"]
    with zipfile.ZipFile(root / "artifacts" / store) as archive:
        assert all(name.startswith(("icons/", "_locales/")) or name in {
            "manifest.json", "distribution.json", "background.js", "i18n.js", "updates.js", "popup.html", "popup.js",
            "options.html", "options.js", "style.css"} for name in archive.namelist())
assert json.loads(read(f"UniShare-Chromium-{version}.zip", "distribution.json"))["store"] == "development"
with zipfile.ZipFile(root / "artifacts" / f"UniShare-Source-{version}.zip") as archive:
    names = archive.namelist()
    assert archive.read(f"UniShare-{version}/SOURCE_REVISION").decode().strip() == plan["sourceRevision"]
    assert not any("AGENTS.md" in name or "DG-" in name or "/artifacts/" in name or "/.git/" in name for name in names)
    assert archive.read(f"UniShare-{version}/integrations/chromium-extension/updates.js") == (root / "integrations/chromium-extension/updates.js").read_bytes().replace(b"\r\n", b"\n")
expected_sums = "".join(f"{item['sha256']}  {Path(item['path']).name}\n" for item in plan["artifacts"])
assert Path(plan["checksumFile"]).read_text() == expected_sums
report = {"version": version, "checks": checks, "nestedPackagesMatch": True, "channelsMatch": True,
          "sourceRevision": plan["sourceRevision"], "publicRevision": plan["publicRevision"],
          "privateSourceFilesExcluded": True, "installersExecuted": 0, "actualStoreUpdateVerified": False}
destination = root / "artifacts/test-results" / f"release-{version}" / "archive-verification.json"
destination.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"artifactsVerified": len(checks), "nestedPackagesMatch": True, "channelsMatch": True,
                  "privateSourceFilesExcluded": True, "installersExecuted": 0}))
