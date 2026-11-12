"""Compare APK payload sizes with the Unity BuildReport next to each APK."""

import argparse
from collections import Counter
import csv
from dataclasses import dataclass
from pathlib import Path
import sys
from zipfile import BadZipFile, ZipFile


PROJECT = Path(__file__).resolve().parents[1]
MIB = 1024 * 1024
FALLBACK_TEXTURES = {f"assets/resources/mapsurfaces/{name}-v1.png"
                     for name in ("grass", "sand", "road", "notice")}


def size(count):
    return f"{count / MIB:.2f} MiB ({count:,} B)"


def zip_group(path):
    if path.startswith("lib/"):
        return f"原生库 / {path.split('/')[1]}"
    if path.startswith("assets/bin/Data/"):
        return "Unity Player 数据"
    if path.startswith(("assets/BaseScenarios/", "assets/Text/", "assets/StoryScene/")):
        return f"StreamingAssets / {path.split('/')[1]}"
    if path.startswith("assets/"):
        return "其他 APK assets"
    if path.startswith("classes") and path.endswith(".dex"):
        return "Android DEX"
    return "清单、Android 资源与签名条目"


def source_group(path):
    parts = path.split("/")
    if parts[0] == "Packages":
        return "/".join(parts[:2])
    if parts[0] != "Assets":
        return "Unity 内置、生成或未标明来源"
    if len(parts) > 3 and parts[1] in {"Resources", "Art", "StreamingAssets"}:
        return "/".join(parts[:3])
    return "/".join(parts[:2])


def totals(entries, group):
    counter = Counter()
    for name, count in entries:
        counter[group(name)] += count
    return counter


@dataclass
class Audit:
    apk: Path
    file_size: int
    zip_entries: list
    raw_entries: list
    packed_assets: list | None

    @classmethod
    def read(cls, apk):
        # Gradle emits UTF-8 names, sometimes without the ZIP language flag.
        try:
            with ZipFile(apk, metadata_encoding="utf-8") as archive:
                files = [entry for entry in archive.infolist() if not entry.is_dir()]
        except UnicodeDecodeError:
            with ZipFile(apk) as archive:
                files = [entry for entry in archive.infolist() if not entry.is_dir()]
        report = apk.with_suffix(".assets.tsv")
        packed = None
        if report.is_file():
            with report.open(encoding="utf-8-sig", newline="") as stream:
                reader = csv.DictReader(stream, delimiter="\t")
                if reader.fieldnames != ["PackedBytes", "SourceAsset"]:
                    raise ValueError(f"无效的 Unity 资源清单：{report}")
                packed = [(row["SourceAsset"].replace("\\", "/"), int(row["PackedBytes"])) for row in reader]
        return cls(apk, apk.stat().st_size, [(i.filename, i.compress_size) for i in files],
                   [(i.filename, i.file_size) for i in files], packed)

    def abi(self):
        return ", ".join(sorted({name.split("/")[1] for name, _ in self.zip_entries
                                 if name.startswith("lib/") and name.endswith(".so")})) or "无"

    def zip_totals(self):
        entries = totals(self.zip_entries, zip_group)
        entries["容器开销（签名块、对齐与 ZIP 结构）"] = self.file_size - sum(entries.values())
        return entries


def table(title, release, baseline):
    print(f"\n## {title}\n")
    if baseline is None:
        print("| 分类 | 当前 |\n| --- | ---: |")
    else:
        print("| 分类 | 基线 | 当前 | 当前减基线 |\n| --- | ---: | ---: | ---: |")
    old = baseline or {}
    for name in sorted(release.keys() | old.keys(), key=lambda n: (-max(release.get(n, 0), old.get(n, 0)), n)):
        current = release.get(name, 0)
        previous = old.get(name, 0)
        row = f"{size(current)}" if baseline is None else f"{size(previous)} | {size(current)} | {current - previous:+,} B"
        print(f"| {name} | {row} |")


def largest(title, entries, count, raw=None):
    if not count or not entries:
        return
    print(f"\n{title}：\n")
    for path, packed in sorted(entries, key=lambda row: (-row[1], row[0]))[:count]:
        suffix = f"，解压后 {size(raw[path])}" if raw is not None else ""
        print(f"- `{path or '（未标明来源）'}`：{size(packed)}{suffix}")


def matched(entries, prefixes=(), names=()):
    return [(path, count) for path, count in entries if path.lower().startswith(prefixes)
            or path.lower().rsplit("/", 1)[-1] in names]


def evidence(audit):
    zip_entries, packed = audit.zip_entries, audit.packed_assets
    performance_files = ("performancetestruninfo.json", "performancetestrunsettings.json")
    return [
        ("开发阶段存档", None, None if packed is None else matched(packed, ("assets/resources/demostages/",))),
        ("普通开发配置场景包", matched(zip_entries, ("assets/storyscene/",)), None),
        ("性能测试元数据", None, None if packed is None else matched(packed, names=performance_files)),
        ("Unity AI 助手及推理包", None, None if packed is None else matched(
            packed, ("packages/com.unity.ai.assistant/", "packages/com.unity.ai.inference/"))),
        ("已被替代的地表和告示牌旧图", None, None if packed is None else
         [(path, count) for path, count in packed if path.lower() in FALLBACK_TEXTURES]),
        ("正式平衡配表", None, None if packed is None else matched(packed, ("assets/resources/demobalance/",))),
        ("正式基地场景数据", matched(zip_entries, ("assets/basescenarios/",)), None),
        ("正式剧情文案", matched(zip_entries, ("assets/text/story-text.json",)), None),
    ]


def report(release, baseline, count):
    print("# Android Demo 包体核查\n")
    for label, audit in (("基线", baseline), ("当前", release)):
        if audit is not None:
            print(f"- {label}：`{audit.apk.name}`，{size(audit.file_size)}，ABI：{audit.abi()}")
    if baseline is not None:
        print(f"- 包体差：{release.file_size - baseline.file_size:+,} B（当前减基线）")
    table("APK 内各类数据（按 ZIP 实际占用）", release.zip_totals(), baseline.zip_totals() if baseline else None)
    largest("当前 APK 最大条目", release.zip_entries, count, dict(release.raw_entries))

    if release.packed_assets is not None:
        compare = totals(baseline.packed_assets, source_group) if baseline and baseline.packed_assets is not None else None
        table("Unity 按源路径归类（构建报告 PackedBytes）", totals(release.packed_assets, source_group), compare)
        print("\nUnity 构建报告按源资源统计，不能与 APK 内 ZIP 字节直接相加。")
        largest("当前包最大的 Unity 源资源", release.packed_assets, count)
    for audit in (baseline, release):
        if audit and audit.packed_assets is None:
            print(f"\n未找到 `{audit.apk.with_suffix('.assets.tsv').name}`；该包只核对 ZIP 可见条目。")

    print("\n## 开发内容与正式数据\n")
    for label, audit in (("基线", baseline), ("当前", release)):
        if audit is None:
            continue
        print(f"{label}：\n")
        for name, zip_items, source_items in evidence(audit):
            items = zip_items if zip_items is not None else source_items
            if items is None:
                print(f"- {name}：缺少 Unity 构建报告，未核对。")
            elif not items:
                print(f"- {name}：未发现。")
            else:
                print(f"- {name}：{len(items)} 项，{size(sum(value for _, value in items))}。")
                for path, value in items[:min(count, 4)]:
                    print(f"  - `{path}`：{size(value)}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk", type=Path, default=PROJECT / "Builds/Android/RPGDemo.apk")
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--top", type=int, default=10, help="List the N largest entries (0 disables these lists)")
    args = parser.parse_args()
    if args.top < 0:
        parser.error("--top must be non-negative")
    try:
        release = Audit.read(args.apk)
        baseline = Audit.read(args.baseline) if args.baseline else None
    except (OSError, BadZipFile, ValueError) as error:
        parser.exit(1, f"{error}\n")
    report(release, baseline, args.top)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
