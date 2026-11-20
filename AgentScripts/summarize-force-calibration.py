"""Summarize the force calibration CSVs by version, split, case, party and supply tier."""
import argparse
import csv
import math
import statistics
from collections import defaultdict
from pathlib import Path


def summarize(sources):
    groups = defaultdict(list)
    seen = set()
    for path in sources:
        with path.open(encoding="utf-8-sig", newline="") as stream:
            for row in csv.DictReader(stream):
                group = tuple(row[k] for k in ("version","split","case_id","party","grade"))
                match = (*group,row["seed"],row["mirror"])
                if match in seen:
                    raise ValueError(f"Duplicate match {match} in {path}")
                seen.add(match)
                groups[group].append(row)
    for group, rows in sorted(groups.items()):
        n=len(rows)
        mean=lambda field: statistics.mean(float(r[field]) for r in rows)
        rate=mean("won")
        center=(rate+1.96**2/(2*n))/(1+1.96**2/n)
        radius=1.96*math.sqrt(rate*(1-rate)/n+1.96**2/(4*n*n))/(1+1.96**2/n)
        result=dict(zip(("version","split","case_id","party","grade"),group))
        result.update(matches=n,win_rate=rate,win_lo=max(0,center-radius),win_hi=min(1,center+radius),
                      seconds=mean("seconds"),hp_loss=mean("own_hp_loss"),hp_loss_fraction=mean("own_loss_fraction"),
                      knocked_down=mean("own_down"),deaths=mean("own_dead"),enemy_hp_lost=mean("enemy_hp_loss"),
                      enemy_hp_lost_fraction=mean("enemy_loss_fraction"),predicted_seconds=mean("pred_seconds"),
                      enemy_captain_def=mean("enemy_captain_def"),enemy_captain_hp_loss=mean("enemy_captain_hp_loss"),
                      enemy_captain_hp=mean("enemy_captain_hp"),round_limits=sum(r["outcome"] in ("RoundLimit","Timeout") for r in rows))
        if "pred_advantage" in rows[0]:
            result.update(advantage=mean("pred_advantage"),predicted_hp_loss=mean("pred_own_loss"),
                          sweep_eligible=sum(float(r["pred_advantage"])>1.5 for r in rows))
        yield {k:round(v,5) if isinstance(v,float) else v for k,v in result.items()}


def paired_comparison(before, after):
    """Only compare matches which share case, roster, gear, split, seed and side."""
    def index(path):
        with path.open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        keys = ("case_id", "party", "grade", "split", "seed", "mirror")
        indexed = {tuple(row[k] for k in keys): row for row in rows}
        if len(indexed) != len(rows):
            raise ValueError(f"Duplicate paired keys in {path}")
        return indexed

    previous, current = index(before), index(after)
    if previous.keys() != current.keys():
        raise ValueError("Each after match must have one before match with identical seed and sides")
    groups = defaultdict(list)
    for key, row in current.items():
        groups[key[:4]].append((previous[key], row))
    measures = ("won", "seconds", "own_hp_loss", "own_loss_fraction", "enemy_loss_fraction", "own_down")
    for group, pairs in sorted(groups.items()):
        result = dict(zip(("case_id", "party", "grade", "split"), group))
        result["paired_matches"] = len(pairs)
        if group[0] == "elite" and pairs[0][0]["version"] == "before299f5ce":
            result["target_change"] = "historical 100 HP proxy vs authored 175 HP elite, both 12 armor"
        fields = measures + (("own_dead",) if group[0] in ("hideout", "defense", "bandit", "second") else ())
        for metric in fields:
            old = statistics.mean(float(before[metric]) for before, _ in pairs)
            new = statistics.mean(float(after[metric]) for _, after in pairs)
            for label, value in (("before", old), ("after", new), ("change", new-old)):
                result[f"{metric}_{label}"] = round(value,5)
        yield result


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("matches",type=Path,nargs="+")
    parser.add_argument("--out",type=Path,required=True)
    parser.add_argument("--before",type=Path,help="Produce a paired comparison against this baseline matches.csv")
    args=parser.parse_args()
    if args.before and len(args.matches) != 1:
        parser.error("Paired comparison takes exactly one candidate matches.csv")
    entries = list(paired_comparison(args.before, args.matches[0]) if args.before else summarize(args.matches))
    if not entries: raise SystemExit("No completed matches")
    with args.out.open("w",newline="",encoding="utf-8-sig") as stream:
        writer=csv.DictWriter(stream,fieldnames=list(dict.fromkeys(k for row in entries for k in row)))
        writer.writeheader()
        writer.writerows(entries)
