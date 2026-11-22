from argparse import ArgumentParser
import csv
from collections import Counter
from math import exp, log
from pathlib import Path


ROLES = ("剑士", "弓兵", "骑兵", "法师")
SCORE = {"a": 1, "b": 0, "timeout": 0.5, "draw": 0.5}


def read(path):
    with path.open(encoding="utf-8-sig", newline="") as source:
        return list(csv.DictReader(source))


def probability(log_ratio):
    return 1 / (1 + exp(-max(-40, min(40, 6 * log_ratio))))


def fit(rows):
    # 共用规模斜率。剑士定为 100，优化其他职业的对数相对值。
    ratings = [0.0] * len(ROLES)
    samples = [(int(r["role_a"]), int(r["role_b"]),
                log(int(r["count_a"]) / int(r["count_b"])), SCORE[r["outcome"]])
               for r in rows]
    for _ in range(1000):
        gradients = [0.0] * len(ROLES)
        for a, b, headcount, outcome in samples:
            error = probability(headcount + ratings[a] - ratings[b]) - outcome
            gradients[a] += error
            gradients[b] -= error
        for role in range(1, len(ROLES)):
            ratings[role] -= 0.4 * (gradients[role] / len(samples) + 0.04 * ratings[role])
    return [round(100 * exp(value), 2) for value in ratings]


def stats(rows):
    counts = Counter(r["outcome"] for r in rows)
    return counts["a"], counts["b"], counts["draw"], counts["timeout"]


def write_report(path, training, checks, points, sensitivity):
    total = len(checks)
    residuals = []
    for r in checks:
        relative = log(float(r["points_a"]) / float(r["points_b"]))
        residuals.append((probability(relative) - SCORE[r["outcome"]]) ** 2)
    score_error = sum(residuals) / total
    neutral_error = sum((0.5 - SCORE[r["outcome"]]) ** 2 for r in checks) / total
    lines = [
        "# 四职业纯兵战力标定",
        "",
        f"按当前规则快照运行 Unity 正式实时模拟。训练 {len(training)} 局，独立种子复验 {total} 局。"
        "普通难度、一级满血普通兵、没有队长，双方军需档相同。每组对调出生侧。"
        "受限地图用居中的对称障碍分出两条通道。90 秒未分胜负记为超时，拟合时按半胜处理。",
        "",
        "## 候选单兵值",
        "",
        "| 职业 | 折算值 | 约 1000 战力人数 | 实际战力 |",
        "| --- | ---: | ---: | ---: |",
    ]
    for role, value in zip(ROLES, points):
        size = min(24, max(1, int(1000 / value + 0.5)))
        lines.append(f"| {role} | {value:.2f} | {size} | {size * value:.2f} |")
    lines += [
        "",
        "剑士设为 100，以训练组的对局胜负和人数比拟合剩余职业，"
        f"固定小幅正则项约束极端偏重。独立样本的胜负概率均方误差为 {score_error:.3f}，各场一律按 50% 估计的基线为 {neutral_error:.3f}。",
        "",
        "## 等名义战力复验",
        "",
        "| 对战 | A 胜 | B 胜 | 平 | 超时 | B 的综合得分率 |",
        "| --- | ---: | ---: | ---: | ---: | ---: |",
    ]
    for a, name_a in enumerate(ROLES):
        for b, name_b in enumerate(ROLES[a + 1:], a + 1):
            group = [r for r in checks if r["role_a"] == str(a) and r["role_b"] == str(b)]
            won_a, won_b, drawn, timed_out = stats(group)
            score = (won_b + (drawn + timed_out) / 2) / len(group)
            lines.append(f"| {name_a}／{name_b} | {won_a} | {won_b} | {drawn} | {timed_out} | {score:.1%} |")
    counter = [r for r in checks if r["role_a"] == "1" and r["role_b"] == "2"]
    wins_a, wins_b, draws, timeouts = stats(counter)
    lines += ["", "### 弓兵与骑兵", ""]
    for terrain, name in (("flat", "平地"), ("obstacle", "受限地形")):
        a, b, d, t = stats(r for r in counter if r["map"] == terrain)
        lines.append(f"- {name}中，弓兵胜 {a}，骑兵胜 {b}，平 {d}，超时 {t}。")
    for grade in range(3):
        a, b, d, t = stats(r for r in counter if r["grade"] == str(grade))
        lines.append(f"- 军需档 {grade} 中，弓兵胜 {a}，骑兵胜 {b}，平 {d}，超时 {t}。")
    if sensitivity:
        first = sensitivity[0]
        a, b, d, t = stats(sensitivity)
        lines += [
            "",
            f"为检查人数取整的影响，另用新种子复验 {len(sensitivity)} 局。"
            f"{first['count_b']} 名骑兵战力 {first['points_b']}，"
            f"{first['count_a']} 名弓兵战力 {first['points_a']}。"
            f"骑兵胜 {b}，弓兵胜 {a}，平局 {d}，超时 {t}。",
        ]
    lines += [
        "",
        f"按最接近 1000 的整数人数，骑兵胜 {wins_b} 局、弓兵胜 {wins_a} 局，"
        f"平局 {draws} 局，超时 {timeouts} 局。两边实际战力见人数表。",
        "",
    ]
    if sensitivity:
        _, wins_rider, _, _ = stats(sensitivity)
        wins_bow = stats(sensitivity)[0]
        if wins_rider <= wins_bow:
            lines.extend([
                "将两队实际战力拉近，并让骑兵总分略低后，骑兵没有赢得更多。"
                "当前单兵值只能作候选，尚不足以稳健表达等战力下的弓骑克制。",
                "",
            ])
    lines += [
        "下一轮可将总体战力和交战克制分别验证。先用单兵分衡量规模，再把各职业对阵的实测得分率与模型预测的差作为候选修正，"
        "用独立种子和混编队伍检查。当前配表、备战战力与扫荡沿用既有值。",
        "",
        "逐局 CSV 保留双方职业与人数、军需档、出生侧、种子、结果、用时及剩余生命。",
        "",
    ]
    path.write_text("\n".join(lines), encoding="utf-8")


def main():
    parser = ArgumentParser()
    parser.add_argument("train", type=Path)
    parser.add_argument("--holdout", type=Path)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--sensitivity", type=Path)
    args = parser.parse_args()
    training = read(args.train)
    points = fit(training)
    if args.holdout and args.report:
        sensitivity = read(args.sensitivity) if args.sensitivity else None
        write_report(args.report, training, read(args.holdout), points, sensitivity)
    print(",".join(f"{p:.2f}" for p in points))


if __name__ == "__main__":
    main()
