"""Per-position gold and last hits at 10/20 minutes from recent pro matches (OpenDota public API)."""
import json
import statistics
import sys
import time
import urllib.request

API = "https://api.opendota.com/api"
OUT = sys.argv[1] if len(sys.argv) > 1 else "opendota_matches.json"
LIMIT = int(sys.argv[2]) if len(sys.argv) > 2 else 80


def get(url):
    for attempt in range(4):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": "dota-dynasty-stats/1.0"})
            with urllib.request.urlopen(request, timeout=30) as r:
                return json.load(r)
        except Exception as e:  # rate limit or transient error
            print(f"  retry {url}: {e}", file=sys.stderr)
            time.sleep(3 + attempt * 3)
    return None


pro = get(f"{API}/proMatches")
matches = []
for m in pro[:LIMIT]:
    d = get(f"{API}/matches/{m['match_id']}")
    time.sleep(1.1)  # free tier: 60 requests per minute
    if not d or not d.get("players") or not d["players"][0].get("gold_t"):
        continue  # not parsed
    matches.append(d)
json.dump(matches, open(OUT, "w"))
print(f"parsed matches: {len(matches)} of {min(LIMIT, len(pro))}")


def positions(team):
    """pos -> player: mid by lane, safe lane split into 1/5 and off lane into 3/4 by gold at 10 min."""
    by_lane = {}
    for p in team:
        by_lane.setdefault(p.get("lane_role"), []).append(p)
    g10 = lambda p: p["gold_t"][10] if len(p["gold_t"]) > 10 else 0
    if len(by_lane.get(2, [])) == 1 and len(by_lane.get(1, [])) == 2 and len(by_lane.get(3, [])) == 2:
        safe = sorted(by_lane[1], key=g10, reverse=True)
        off = sorted(by_lane[3], key=g10, reverse=True)
        return {1: safe[0], 2: by_lane[2][0], 3: off[0], 4: off[1], 5: safe[1]}
    return None  # unusual lanes (trilane, jungle): skip


rows = {pos: {"g10": [], "g20": [], "lh10": [], "lh20": [], "k": [], "d": []} for pos in range(1, 6)}
kills, durations, skipped = [], [], 0
for d in matches:
    durations.append(d["duration"] / 60)
    kills.append((d.get("radiant_score") or 0) + (d.get("dire_score") or 0))
    for radiant in (True, False):
        team = [p for p in d["players"] if p["isRadiant"] == radiant]
        pos = positions(team)
        if pos is None:
            skipped += 1
            continue
        for k, p in pos.items():
            r = rows[k]
            r["k"].append(p["kills"])
            r["d"].append(p["deaths"])
            if len(p["gold_t"]) > 10:
                r["g10"].append(p["gold_t"][10]); r["lh10"].append(p["lh_t"][10])
            if len(p["gold_t"]) > 20:
                r["g20"].append(p["gold_t"][20]); r["lh20"].append(p["lh_t"][20])

med = lambda xs: statistics.median(xs) if xs else float("nan")
avg = lambda xs: statistics.mean(xs) if xs else float("nan")
print(f"matches {len(matches)}, teams skipped (unusual lanes) {skipped}")
print(f"kills per match: mean {avg(kills):.1f}, median {med(kills):.0f}; duration: mean {avg(durations):.1f} min, median {med(durations):.1f}")
print("pos  gold@10  gold@20   LH@10  LH@20   kills deaths  (means; gold = total earned, ~net worth)")
for pos, r in rows.items():
    print(f"{pos:>3} {avg(r['g10']):8.0f} {avg(r['g20']):8.0f} {avg(r['lh10']):7.1f} {avg(r['lh20']):6.1f} {avg(r['k']):7.1f} {avg(r['d']):6.1f}   n={len(r['g10'])}")
