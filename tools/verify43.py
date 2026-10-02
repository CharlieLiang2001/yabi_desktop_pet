"""Summarize already-collected native evidence without rerunning expensive tests."""
import hashlib
import json
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path
from statistics import median

PROJECT = Path(__file__).resolve().parents[1]
EVIDENCE = PROJECT / "bin" / "validation43"


def timings(path):
    starts = {}
    durations = defaultdict(list)
    counts = Counter()
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        timestamp, event, clip = line.split()[:3]
        stamp = datetime.fromisoformat(timestamp.replace("Z", "+00:00"))
        counts[event + ":" + clip] += 1
        if event == "start":
            starts[clip] = stamp
        elif event == "complete" and clip in starts:
            durations[clip].append((stamp - starts.pop(clip)).total_seconds())
    return counts, durations


def main():
    errors = []
    standard_count, standard = timings(EVIDENCE / "playback-natural.log")
    eco_count, eco = timings(EVIDENCE / "playback-eco.log")
    cycle_count, _ = timings(EVIDENCE / "playback-cycle.log")
    required = ("feed", "pet", "groom", "scratch", "notice_all", "notice_left", "notice_right", "notice_up")
    timing_report = {}
    for clip in required:
        if min(len(standard[clip]), len(eco[clip])) < 2:
            errors.append(f"Fewer than two complete runs: {clip}")
            continue
        a, b = median(standard[clip]), median(eco[clip])
        timing_report[clip] = {"standardSeconds": round(a, 3), "ecoSeconds": round(b, 3), "differenceSeconds": round(abs(a-b), 3)}
        if abs(a-b) > .25:
            errors.append(f"Power mode duration drift exceeds 250 ms: {clip}")
    completed_cycles = cycle_count["complete:sleep_to_sit"]
    if completed_cycles < 2:
        errors.append("Fewer than two automatic sleep/wake cycles")
    if any(key.startswith("failed:") for key in cycle_count):
        errors.append("Uninjected decode failure in automatic cycle")
    for name, counts in (("standard", standard_count), ("eco", eco_count)):
        if sum(value for key, value in counts.items() if key.startswith("failed:")) != 1 or counts["failed:groom"] != 1:
            errors.append(f"Expected exactly one injected groom failure in {name}")
    asset = json.loads((PROJECT / "assets/generated/validation43.json").read_text(encoding="utf-8"))
    with (PROJECT / "assets/generated/yabi-animations-v43.zip").open("rb") as stream:
        archive_hash = hashlib.file_digest(stream, "sha256").hexdigest().upper()
    if not asset["ok"] or archive_hash != asset["zip"]["sha256"]:
        errors.append("Asset validation or archive hash mismatch")
    report = {"ok": not errors, "failures": errors, "automaticCyclesCompleted": completed_cycles,
              "powerModeTimingComparison": timing_report, "newSourceFrames": sum(c["frameCount"] for c in asset["clips"].values()),
              "assetArchiveSha256": archive_hash}
    (EVIDENCE / "verification-summary.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if not errors else 1


if __name__ == "__main__":
    raise SystemExit(main())
