#!/usr/bin/env python3
"""Writes a demo folder of anonymised DeviceDossier reports: six PCs with one to four audits each.

Used for screenshots and for trying DriftWatch without real audits. Every report matches
DeviceDossier's published schema (tests/report-schema.xsd).

Usage: python3 tests/make_demo.py [output-folder]
"""
import copy
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_fixtures import base_report, changed_report, set_field, table, set_title, write  # noqa: E402


def rename(rep, device, user, laptop=True):
    rep["attrs"]["device"] = device
    rep["attrs"]["runAs"] = device + "\\it.auditor"
    rep["attrs"]["laptop"] = "true" if laptop else "false"
    set_field(rep, "device", "Device Name", device)
    set_field(rep, "device", "Device Type", "Laptop" if laptop else "Desktop")
    for r in table(rep, "security", "Local Administrative Accounts")[3]:
        r[0] = device + "\\" + r[0].split("\\")[-1]
    for r in table(rep, "drives")[3]:
        r[0] = user
    for r in table(rep, "extensions")[3]:
        r[1] = user
    if not laptop:
        rep["sections"] = [s for s in rep["sections"] if s[0] != "power"]
    return rep


def at(rep, iso):
    rep = copy.deepcopy(rep)
    rep["attrs"]["generated"] = iso
    return rep


def add_software(rep, name, version, publisher, date):
    sw = table(rep, "software")
    rows = [r[1:] for r in sw[3]] + [[name, version, publisher, date, ""]]
    sw[3][:] = [[str(i + 1)] + r for i, r in enumerate(rows)]
    set_title(rep, "software", "INSTALLED SOFTWARE - Total: %d apps" % len(rows))


def patch(rep, month, day):
    set_field(rep, "security", "Latest Security Update",
              "2026-%02d Cumulative Update for Windows 11 Version 25H2 for x64-based Systems - Installed: %s" % (month, day),
              "Patch & Update Level")


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "demo")
    os.makedirs(out, exist_ok=True)
    base = base_report()

    def save(rep):
        dev = rep["attrs"]["device"]
        g = rep["attrs"]["generated"]
        stamp = g[8:10] + g[5:7] + g[0:4] + "_" + g[11:13] + g[14:16]
        folder = os.path.join(out, dev)
        os.makedirs(folder, exist_ok=True)
        write(rep, os.path.join(folder, "IT_Audit_%s_%s.xml" % (dev, stamp)))

    # 1. The fixture laptop: three audits, the last one with risky changes.
    early = at(base, "2026-10-21T10:15:00")
    patch(early, 9, "16-09-2026")
    set_field(early, "baseline", "Defender Signatures", "1.419.902.0 (updated 20-10-2026)")
    save(early)
    save(base)
    save(changed_report(base))

    # 2. A well-kept finance laptop: four audits, routine updates only.
    fin = rename(copy.deepcopy(base), "FIN-LAPTOP-03", "accounts.user")
    set_field(fin, "device", "Model Name", "ThinkPad T14s Gen 5")
    for i, (when, build, chrome) in enumerate([("2026-10-01T09:00:00", "26200.6584", "129.0.6668.90"),
                                              ("2026-10-15T09:05:00", "26200.6725", "130.0.6723.70"),
                                              ("2026-11-01T09:02:00", "26200.6899", "131.0.6778.86"),
                                              ("2026-11-17T09:10:00", "26200.7019", "131.0.6778.140")]):
        r = at(fin, when)
        set_field(r, "os", "Build Number", build)
        table(r, "software")[3][0][2] = chrome
        patch(r, int(when[5:7]), "%s-%s-2026" % (when[8:10], when[5:7]))
        set_field(r, "baseline", "Defender Signatures", "1.421.%d.0 (updated %s-%s-2026)" % (100 + i, when[8:10], when[5:7]))
        if i == 3:
            add_software(r, "Microsoft Teams (work or school)", "24295.605.3225.8804", "Microsoft Corporation", "16-11-2026")
        save(r)

    # 3. A reception desktop: weak settings, one old audit only (stale).
    rec = rename(copy.deepcopy(base), "RECEPTION-PC", "front.desk", laptop=False)
    rec = at(rec, "2026-09-08T16:40:00")
    set_field(rec, "device", "Manufacturer", "HP")
    set_field(rec, "device", "Model Name", "ProDesk 400 G7")
    bl = table(rec, "security", "BitLocker Status")
    bl[3][:] = [["C:", "Off", "FullyDecrypted", "0%", "None"]]
    set_field(rec, "baseline", "Remote Desktop", "Enabled")
    set_field(rec, "baseline", "Secure Boot", "Off")
    set_field(rec, "baseline", "Defender Signatures", "1.417.20.0 (updated 12-08-2026)")
    patch(rec, 6, "11-06-2026")
    add_software(rec, "TeamViewer", "15.58.5", "TeamViewer Germany GmbH", "02-07-2026")
    set_field(rec, "os", "Activation Status", "Notification mode")
    save(rec)

    # 4. An engineering workstation: three audits, drifting the wrong way.
    eng = rename(copy.deepcopy(base), "ENG-WS-02", "eng.lead", laptop=False)
    set_field(eng, "device", "Manufacturer", "Dell Inc.")
    set_field(eng, "device", "Model Name", "Precision 3680 Tower")
    a = at(eng, "2026-10-06T11:00:00")
    patch(a, 9, "18-09-2026")
    set_field(a, "baseline", "Defender Signatures", "1.420.10.0 (updated 05-10-2026)")
    save(a)
    b = at(eng, "2026-10-27T11:20:00")
    patch(b, 10, "15-10-2026")
    set_field(b, "baseline", "Defender Signatures", "1.420.880.0 (updated 26-10-2026)")
    add_software(b, "Wireshark 4.4.1 x64", "4.4.1", "The Wireshark developer community", "21-10-2026")
    save(b)
    c = at(b, "2026-11-16T11:05:00")
    patch(c, 11, "12-11-2026")
    set_field(c, "baseline", "Defender Signatures", "1.421.300.0 (updated 15-11-2026)")
    set_field(c, "baseline", "SMBv1 Protocol", "Enabled")
    table(c, "security", "Local Administrative Accounts")[3].append(["ENG-WS-02\\eng.lead", "User", "Local"])
    table(c, "storage")[3][1][7] = "71"
    save(c)

    # 5. An HR laptop: two audits; a torrent client appears and definitions go stale.
    hr = rename(copy.deepcopy(base), "HR-LAPTOP-05", "hr.officer")
    set_field(hr, "device", "Model Name", "IdeaPad Slim 5 14IMH9")
    a = at(hr, "2026-10-29T14:00:00")
    patch(a, 10, "15-10-2026")
    set_field(a, "baseline", "Defender Signatures", "1.420.900.0 (updated 28-10-2026)")
    save(a)
    b = at(a, "2026-11-18T15:30:00")
    add_software(b, "qBittorrent 5.0.1", "5.0.1", "The qBittorrent project", "09-11-2026")
    set_field(b, "baseline", "Defender Signatures", "1.420.900.0 (updated 28-10-2026)")
    set_field(b, "baseline", "Pending Restart", "Yes - a restart is waiting to finish updates")
    save(b)

    # 6. A management tablet: one audit, in good shape.
    mg = rename(copy.deepcopy(base), "MGMT-SURFACE", "director")
    mg = at(mg, "2026-11-12T10:00:00")
    set_field(mg, "device", "Manufacturer", "Microsoft Corporation")
    set_field(mg, "device", "Model Name", "Surface Laptop 7")
    patch(mg, 11, "12-11-2026")
    set_field(mg, "baseline", "Defender Signatures", "1.421.250.0 (updated 11-11-2026)")
    table(mg, "security", "Local Administrative Accounts")[3][:] = [["MGMT-SURFACE\\Administrator", "User", "Local"]]
    save(mg)

    print("demo reports written to", out)


if __name__ == "__main__":
    main()
