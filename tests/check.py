#!/usr/bin/env python3
"""End-to-end tests for DriftWatch's command line.

Builds fresh fixtures, runs DriftWatch.exe on them and checks the exit codes, the JSON output and the
HTML reports. Works on Windows (runs the exe directly) and on Linux/macOS (runs it with Mono).

Usage: python3 tests/check.py [path/to/DriftWatch.exe]
"""
import json
import os
import subprocess
import sys
import tempfile
from html.parser import HTMLParser

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
EXE = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "build", "DriftWatch.exe")
FAILURES = []


def run(*args):
    cmd = [EXE] if os.name == "nt" else ["mono", EXE]
    p = subprocess.run(cmd + list(args), capture_output=True, text=True, encoding="utf-8")
    return p.returncode, p.stdout + p.stderr


def check(name, ok, detail=""):
    print(("PASS  " if ok else "FAIL  ") + name + ("" if ok else "  -> " + detail))
    if not ok:
        FAILURES.append(name)


def main():
    work = tempfile.mkdtemp(prefix="driftwatch-")
    fx = os.path.join(work, "fixtures")
    subprocess.run([sys.executable, os.path.join(HERE, "make_fixtures.py"), fx], check=True, capture_output=True)
    old = os.path.join(fx, "IT_Audit_DEMO-LAPTOP-01_04112026_1030.xml")
    new = os.path.join(fx, "IT_Audit_DEMO-LAPTOP-01_18112026_1405.xml")
    same_a = os.path.join(fx, "IT_Audit_DEMO-DESK-07_05112026_0915.xml")
    same_b = os.path.join(fx, "IT_Audit_DEMO-DESK-07_19112026_0920.xml")

    # Every fixture must match DeviceDossier's published schema, so the tests reflect real reports.
    try:
        from lxml import etree
        schema = etree.XMLSchema(etree.parse(os.path.join(HERE, "report-schema.xsd")))
        for f in sorted(os.listdir(fx)):
            check("fixture matches DeviceDossier schema: " + f, schema.validate(etree.parse(os.path.join(fx, f))), str(schema.error_log))
    except ImportError:
        print("SKIP  schema validation (pip install lxml to enable)")

    out_json = os.path.join(work, "pair.json")
    out_html = os.path.join(work, "pair.html")
    code, text = run(old, new, "--json", out_json, "--html", out_html)
    check("pair: exit code 1 when high or critical changes exist", code == 1, "got %d: %s" % (code, text))
    data = json.load(open(out_json, encoding="utf-8"))
    titles = {c["title"]: c["severity"] for c in data["changes"]}

    expected = {
        "New local administrator: DEMO-LAPTOP-01\\support.temp": "Critical",
        "BitLocker protection turned off on C:": "Critical",
        "Firewall turned off (public networks)": "Critical",
        "Real-time protection turned off": "Critical",
        "Remote Desktop turned on": "High",
        "Remote-access software installed: AnyDesk 9.0.4": "High",
        "Memory added: RAM in DIMM 1 (16 GB)": "Medium",
        "Software removed: Dropbox 211.4.6008": "Low",
        "Updated: Google Chrome (131.0.6778.86 → 131.0.6778.140)": "Info",
        "Monitor connected: DELL U2723QE": "Info",
        "New security update installed": "Info",
    }
    for title, sev in expected.items():
        check("finds: " + title, titles.get(title) == sev, "got %r" % titles.get(title))
    check("finds the unrecognised mapped server as High", any(c["severity"] == "High" and "unrecognised server" in c["title"] for c in data["changes"]))
    check("finds the falling drive health as Medium", any(c["severity"] == "Medium" and "Drive health falling fast" in c["title"] for c in data["changes"]))
    check("verdict is Action needed", data["verdict"] == "Action needed", data["verdict"])
    check("changes are ordered most severe first", [c["severity"] for c in data["changes"]] == sorted([c["severity"] for c in data["changes"]], key=["Critical", "High", "Medium", "Low", "Info"].index))
    check("ignores run-to-run noise (last boot, current charge, list numbering)", not any(c["field"] in ("Last Boot", "Current Charge (%)", "No.") for c in data["changes"]))
    check("rows matched by identity, not position (only 2 software adds and 1 removal)",
          sum(1 for c in data["changes"] if c["section"] == "software" and c["kind"] == "Added") == 2 and
          sum(1 for c in data["changes"] if c["section"] == "software" and c["kind"] == "Removed") == 1)
    check("no notices for two admin audits of the same device", data["notices"] == [], str(data["notices"]))

    code, text = run(same_a, same_b, "--json", out_json)
    data = json.load(open(out_json, encoding="utf-8"))
    check("identical audits: exit code 0", code == 0, "got %d" % code)
    check("identical audits: no changes", data["changes"] == [], str(data["changes"][:3]))

    code, text = run(new, old, "--json", out_json, "--quiet")
    data = json.load(open(out_json, encoding="utf-8"))
    check("reports given newest first are swapped", data["baseline"]["file"].endswith("04112026_1030.xml") and any("swapped" in n for n in data["notices"]), str(data["notices"]))
    check("--quiet prints nothing", text.strip() == "", text)

    code, _ = run(old, new, "--quiet", "--fail-on", "critical")
    check("--fail-on critical: exit 1 (criticals exist)", code == 1, "got %d" % code)
    code, _ = run(same_a, same_b, "--quiet", "--fail-on", "info")
    check("--fail-on info with no changes: exit 0", code == 0, "got %d" % code)

    code, _ = run(old, same_a, "--json", out_json, "--quiet")
    data = json.load(open(out_json, encoding="utf-8"))
    check("warns when the reports are from different devices", any("different devices" in n for n in data["notices"]), str(data["notices"]))

    fleet_json = os.path.join(work, "fleet.json")
    fleet_html = os.path.join(work, "fleet.html")
    code, text = run("--folder", fx, "--json", fleet_json, "--html", fleet_html)
    fleet = json.load(open(fleet_json, encoding="utf-8"))
    check("fleet: exit code 1 when any device has high changes", code == 1, "got %d" % code)
    check("fleet: two devices found and compared", fleet["devices"] == 2 and len(fleet["comparisons"]) == 2, text)
    check("fleet: riskiest device listed first", fleet["comparisons"][0]["device"] == "DEMO-LAPTOP-01")

    code, _ = run(old, "--json", out_json)
    check("one report only: exit code 2 (invalid arguments)", code == 2, "got %d" % code)
    code, _ = run("--fail-on", "urgent", old, new)
    check("bad --fail-on value: exit code 2", code == 2, "got %d" % code)
    code, _ = run(os.path.join(HERE, "report-schema.xsd"), old)
    check("not a DeviceDossier report: exit code 3", code == 3, "got %d" % code)
    code, _ = run(os.path.join(work, "missing.xml"), old)
    check("missing file: exit code 3", code == 3, "got %d" % code)

    for path in (out_html, fleet_html):
        try:
            HTMLParser().feed(open(path, encoding="utf-8").read())
            check("HTML report parses: " + os.path.basename(path), True)
        except Exception as ex:
            check("HTML report parses: " + os.path.basename(path), False, str(ex))
    html = open(out_html, encoding="utf-8").read()
    check("HTML report is self-contained (no external links)", "http://" not in html.replace("http://www.w3.org", "") and "src=\"http" not in html)

    # Security posture of a single audit.
    post_json = os.path.join(work, "posture.json")
    code, text = run("--posture", new, "--json", post_json, "--min-score", "70")
    post = json.load(open(post_json, encoding="utf-8"))
    status = {c["id"]: c["status"] for c in post["checks"]}
    check("posture: risky audit scores below 70 (exit 1 with --min-score 70)", code == 1 and post["score"] < 70, "%d %s" % (code, post["score"]))
    check("posture: real-time protection off fails", status.get("rtp") == "Fail", str(status.get("rtp")))
    check("posture: BitLocker off on C: fails", status.get("blos") == "Fail", str(status.get("blos")))
    check("posture: public firewall off fails", status.get("fw") == "Fail", str(status.get("fw")))
    check("posture: AnyDesk fails the software check", status.get("software") == "Fail", str(status.get("software")))
    check("posture: Remote Desktop on is a warning", status.get("rdp") == "Warning", str(status.get("rdp")))
    code, _ = run("--posture", old, "--json", post_json, "--min-score", "70", "--quiet")
    post = json.load(open(post_json, encoding="utf-8"))
    check("posture: healthy audit scores 90 or more (exit 0)", code == 0 and post["score"] >= 90, "%d %s" % (code, post["score"]))

    # Policy: banned software, approved software, acknowledged changes.
    policy = os.path.join(work, "policy.json")
    with open(policy, "w", encoding="utf-8") as f:
        json.dump({"bannedSoftware": ["python*"], "approvedSoftware": ["AnyDesk"], "minimumScore": 60,
                   "acknowledged": ["demo-laptop-01|baseline||remote desktop|remote desktop|changed|enabled"]}, f)
    code, _ = run(old, new, "--json", out_json, "--policy", policy, "--quiet")
    data = json.load(open(out_json, encoding="utf-8"))
    by_title = {c["title"]: c for c in data["changes"]}
    check("policy: banned software added is High", any(c["severity"] == "High" and c["title"].startswith("Banned software installed: Python") for c in data["changes"]), str([c["title"] for c in data["changes"] if "Python" in c["title"]]))
    any_desk = [c for c in data["changes"] if "AnyDesk" in c["title"]]
    check("policy: approved remote-access tool lowered to Low", len(any_desk) == 1 and any_desk[0]["severity"] == "Low" and any_desk[0]["policyNote"], str(any_desk))
    rdp = by_title.get("Remote Desktop turned on")
    check("policy: acknowledged change is kept but marked", rdp is not None and rdp["acknowledged"] is True, str(rdp))
    check("policy: acknowledged change leaves the High count", data["counts"]["High"] == 2, str(data["counts"]))
    code, _ = run("--posture", new, "--policy", policy, "--quiet")
    check("policy: minimumScore from the policy sets the posture exit code", code == 1, "got %d" % code)
    with open(policy, "w", encoding="utf-8") as f:
        f.write("{ not json")
    code, _ = run(old, new, "--policy", policy, "--quiet")
    check("policy: broken policy file gives exit code 3", code == 3, "got %d" % code)

    # CSV and Markdown.
    csv_path = os.path.join(work, "pair.csv")
    md_path = os.path.join(work, "pair.md")
    run(old, new, "--csv", csv_path, "--markdown", md_path, "--quiet")
    import csv
    rows = list(csv.reader(open(csv_path, encoding="utf-8-sig", newline="")))
    check("CSV: header plus one row per change", rows[0][0] == "Device" and len(rows) - 1 == len(titles), "%d rows for %d changes" % (len(rows) - 1, len(titles)))
    md = open(md_path, encoding="utf-8").read()
    check("Markdown: summary names the verdict and the score", "Action needed" in md and "Security score" in md, md[:200])
    code, _ = run("--folder", fx, "--csv", csv_path, "--min-score", "95", "--quiet")
    rows = list(csv.reader(open(csv_path, encoding="utf-8-sig", newline="")))
    check("fleet CSV: one row per device", len(rows) == 3, str(len(rows)))
    check("fleet: --min-score 95 gives exit 1", code == 1, "got %d" % code)
    code, _ = run("--min-score", "250", old, new)
    check("bad --min-score value: exit code 2", code == 2, "got %d" % code)

    print()
    if FAILURES:
        print("%d test(s) failed." % len(FAILURES))
        sys.exit(1)
    print("All tests passed.")


if __name__ == "__main__":
    main()
