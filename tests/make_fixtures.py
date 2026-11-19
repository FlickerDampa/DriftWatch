#!/usr/bin/env python3
"""Builds anonymised DeviceDossier XML reports for DriftWatch's tests and demos.

Two audits of the same demo laptop, two weeks apart, with a realistic mix of changes:
risky ones (a new local admin, Defender real-time protection off, BitLocker off on C:, the
public firewall off, Remote Desktop on, an unrecognised mapped server, remote-access software)
and routine ones (updates, a new monitor, a RAM upgrade, browser extension changes).

A third audit of a second PC with no changes checks that identical reports produce nothing.
All names, serials and addresses are made up (192.0.2.x and 198.51.100.x are documentation ranges).

Usage: python3 make_fixtures.py OUTPUT_DIR
"""
import copy
import os
import sys
import xml.etree.ElementTree as ET


def field_block(fields, caption=None):
    return ("fields", caption, list(fields))


def table_block(columns, rows, caption=None):
    return ("table", caption, list(columns), [list(r) for r in rows])


def base_report():
    software = [
        ["Google Chrome", "131.0.6778.86", "Google LLC", "02-10-2026", "C:\\Program Files\\Google\\Chrome\\Application"],
        ["Microsoft Edge", "131.0.2903.70", "Microsoft Corporation", "28-10-2026", "C:\\Program Files (x86)\\Microsoft\\Edge\\Application"],
        ["Microsoft 365 Apps for business - en-us", "16.0.18129.20116", "Microsoft Corporation", "15-09-2026", "C:\\Program Files\\Microsoft Office"],
        ["7-Zip 24.08 (x64)", "24.08", "Igor Pavlov", "11-08-2026", "C:\\Program Files\\7-Zip\\"],
        ["Adobe Acrobat (64-bit)", "24.004.20220", "Adobe", "03-09-2026", "C:\\Program Files\\Adobe\\Acrobat DC\\"],
        ["Zoom Workplace (64-bit)", "6.2.5", "Zoom Video Communications, Inc.", "20-09-2026", "C:\\Program Files\\Zoom\\bin\\"],
        ["Notepad++ (64-bit x64)", "8.6.9", "Notepad++ Team", "14-07-2026", "C:\\Program Files\\Notepad++"],
        ["VLC media player", "3.0.20", "VideoLAN", "01-06-2026", "C:\\Program Files\\VideoLAN\\VLC"],
        ["Microsoft Visual C++ 2015-2022 Redistributable (x64)", "14.40.33810.0", "Microsoft Corporation", "05-05-2026", ""],
        ["GstarCAD 2025", "25.0.2", "GstarCAD", "12-08-2026", "C:\\Program Files\\Gstarsoft\\GstarCAD2025"],
        ["Dropbox", "211.4.6008", "Dropbox, Inc.", "18-09-2026", "C:\\Program Files (x86)\\Dropbox\\Client"],
    ]
    return {
        "attrs": {"schema": "1", "tool": "DeviceDossier 1.0.0-beta.1", "generated": "2026-11-04T10:30:00",
                  "device": "DEMO-LAPTOP-01", "runAs": "DEMO-LAPTOP-01\\it.auditor", "administrator": "true", "laptop": "true"},
        "sections": [
            ("asset", "0", "ASSET RECORD", "Ok", None, [field_block([
                ("Asset Tag", "IT-0042"), ("Assigned To", "Finance team"), ("Location / Department", "HQ - Level 3"), ("Notes", "N/A")])]),
            ("device", "1", "DEVICE INFORMATION", "Ok", None, [field_block([
                ("Device Type", "Laptop"), ("Device Name", "DEMO-LAPTOP-01"), ("Manufacturer", "LENOVO"), ("Model", "83DF"),
                ("Model Name", "Legion Pro 7 16IRX9H"), ("Domain / Workgroup", "WORKGROUP"), ("BIOS Serial Number", "PF0DEMO1"),
                ("BIOS Version", "N0CN31WW"), ("BIOS Release Date", "12-03-2026"), ("System UUID", "4C4C4544-0042-3010-8051-B2C04F4D3132"),
                ("Asset Tag", "N/A")])]),
            ("os", "2", "OPERATING SYSTEM AND ACTIVATION", "Ok", None, [field_block([
                ("OS Name", "Microsoft Windows 11 Pro"), ("Version", "10.0.26200"), ("Display Version", "25H2"), ("Build Number", "26200.6899"),
                ("Architecture", "64-bit"), ("Install Date", "11-05-2026"), ("Last Boot", "04-11-2026 08:12"), ("Registered User", "it.auditor"),
                ("Activation Status", "Licensed"), ("Windows Key", "XXXXX-XXXXX-XXXXX-XXXXX-7J4QK"), ("Product Key Ch.", "OEM:DM")])]),
            ("cpu", "3", "PROCESSOR (CPU) DETAILS", "Ok", None, [table_block(
                ["CPU Model", "Manufacturer", "Cores", "Logical Processors", "Max Speed (MHz)", "Current Speed (MHz)", "CPU ID / Serial", "Socket", "Architecture", "L2 Cache (KB)", "L3 Cache (KB)"],
                [["13th Gen Intel(R) Core(TM) i9-13900HX", "GenuineIntel", "24", "32", "2200", "2200", "BFEBFBFF000B0671", "U3E1", "x64", "32768", "36864"]])]),
            ("ram", "4", "RAM DETAILS - Total: 16 GB - Sticks: 1", "Ok", None, [table_block(
                ["Stick No.", "Capacity (GB)", "Manufacturer", "Part Number", "Serial Number", "Speed (MHz)", "Memory Type", "Slot / Location", "Bank", "Form Factor"],
                [["1", "16", "Samsung", "M425R2GA3BB0-CWMOD", "1F2E3D4C", "5600", "DDR5", "DIMM 0", "BANK 0", "SODIMM"]])]),
            ("gpu", "5", "GPU DETAILS", "Ok", None, [table_block(
                ["GPU Name", "VRAM", "Driver Version", "Driver Date", "Video Proc", "Resolution", "Refresh Rate", "Device ID (DEV)", "Vendor ID (VEN)", "GPU-UUID", "VBIOS Version"],
                [["NVIDIA GeForce RTX 4080 Laptop GPU", "12 GB", "32.0.15.6094", "14-08-2026", "NVIDIA GeForce RTX 4080 Laptop GPU", "2560 x 1600", "240 Hz", "27E0", "10DE", "GPU-5d1e0c36-demo", "95.04.3A.00.21"],
                 ["Intel(R) UHD Graphics", "2 GB", "31.0.101.5186", "20-02-2026", "Intel(R) UHD Graphics Family", "N/A", "N/A", "A788", "8086", "N/A", "N/A"]])]),
            ("storage", "6", "STORAGE / DRIVES", "Ok", None, [table_block(
                ["Drive Model", "Serial Number", "Size (GB)", "Interface", "Media Type", "Partitions", "Firmware", "Health (%)"],
                [["SAMSUNG MZVL21T0HCLR-00BL2", "S641NX0T123456", "953.87", "NVMe", "SSD", "4", "SL2QFXV7", "100"],
                 ["WD Blue SN580 1TB", "24123K801234", "931.51", "NVMe", "SSD", "1", "281010WD", "98"]])]),
            ("motherboard", "6b", "MOTHERBOARD DETAILS", "Ok", None, [field_block([
                ("Manufacturer", "LENOVO"), ("Model / Product", "LNVNB161216"), ("Serial Number", "PF0DEMO1MB"), ("Version", "SDK0T76530 WIN"), ("Status", "OK")])]),
            ("power", "6c", "BATTERY DETAILS", "Ok", None, [table_block(
                ["Battery Name", "Device ID", "Health (%)", "Design Capacity (mWh)", "Full Charge Capacity (mWh)", "Current Charge (%)", "Status", "Design Voltage (mV)"],
                [["L22B4PC1", "33Celxpert L22B4PC1", "94", "99990", "93990", "81", "Charging", "15480"]])]),
            ("network", "7", "NETWORK ADAPTERS AND MAC ADDRESSES", "Ok", None, [table_block(
                ["Adapter", "MAC Address", "IP Address", "Subnet", "Gateway", "DHCP", "DNS"],
                [["Intel(R) Wi-Fi 6E AX211 160MHz", "A0:B3:39:12:34:56", "192.0.2.45", "255.255.255.0", "192.0.2.1", "Yes", "192.0.2.1"],
                 ["Realtek PCIe 2.5GbE Family Controller", "08:BF:B8:65:43:21", "N/A", "N/A", "N/A", "Yes", "N/A"]])]),
            ("drives", "8", "MAPPED NETWORK DRIVES (SERVER AUDIT)", "Ok", None, [table_block(
                ["User Profile", "Drive Letter", "Status", "Server IP", "Matches Known Server List", "Subfolder Path", "Full UNC Path"],
                [["finance.user", "S:", "Connected", "192.0.2.10", "Yes - File server", "Engineering 2", "\\\\192.0.2.10\\Engineering 2"],
                 ["finance.user", "M:", "Connected", "192.0.2.20", "Yes - NAS", "Management", "\\\\192.0.2.20\\Management"]],
                caption="Known servers checked: 192.0.2.10, 192.0.2.20")]),
            ("security", "9", "SECURITY", "Ok", None, [
                table_block(["Product Name", "Details"],
                            [["Microsoft Defender Antivirus", "Registered with Windows Security Center | Real-time protection: On | Antimalware service: On | Mode: Normal"]],
                            caption="Installed EDR / Antivirus"),
                table_block(["Drive", "Protection Status", "Volume Status", "Encryption %", "Key Protector(s)"],
                            [["C:", "On", "FullyEncrypted", "100%", "Tpm, RecoveryPassword"],
                             ["D:", "On", "FullyEncrypted", "100%", "RecoveryPassword, ExternalKey"]],
                            caption="BitLocker Status"),
                field_block([("Latest Security Update", "2026-10 Cumulative Update for Windows 11 Version 25H2 for x64-based Systems - Installed: 15-10-2026"),
                             ("Latest Feature Update", "N/A - none found in update history"),
                             ("Latest Windows (Quality) Update", "2026-10 Cumulative Update for .NET Framework 3.5 and 4.8.1 - Installed: 16-10-2026"),
                             ("Latest General/Suite Update", "Update for Windows Security platform - KB5007651 - Installed: 30-10-2026")],
                            caption="Patch & Update Level"),
                table_block(["Account Name", "Type", "Source"],
                            [["DEMO-LAPTOP-01\\Administrator", "User", "Local"], ["DEMO-LAPTOP-01\\it.auditor", "User", "Local"]],
                            caption="Local Administrative Accounts")]),
            ("software", "10", "INSTALLED SOFTWARE - Total: %d apps" % len(software), "Ok", None, [table_block(
                ["No.", "Software Name", "Version", "Publisher", "Install Date", "Install Location"],
                [[str(i + 1)] + s for i, s in enumerate(software)])]),
            ("extensions", "10b", "BROWSER EXTENSIONS - Total: 3", "Ok", None, [table_block(
                ["Browser", "User Profile", "Extension Name", "Version", "Extension ID"],
                [["Chrome", "finance.user", "Google Docs Offline", "1.86.1", "ghbmnnjooekpmoecnnnilnnbdlolhkhi"],
                 ["Chrome", "finance.user", "uBlock Origin", "1.60.0", "cjpalhdlnbpafiamejdnhcphjbkeiagm"],
                 ["Edge", "finance.user", "Microsoft Editor", "1.34.0", "gpaiobkfhnonedkhhfjpmhdalgeoebfa"]])]),
            ("monitors", "11", "MONITORS / DISPLAYS", "Ok", None, [table_block(
                ["Monitor", "Manufacturer", "Product Code", "Serial Number", "Manufactured", "Size", "Connection", "Active"],
                [["Built-in display", "BOE", "0B9A", "N/A", "Week 10, 2026", "16\"", "Internal", "Yes"]])]),
            ("baseline", "12", "SECURITY BASELINE", "Ok", None, [field_block([
                ("Firmware Type", "UEFI"), ("Secure Boot", "On"), ("TPM", "Present - TPM 2.0, enabled, activated"),
                ("Firewall - Domain Profile", "On"), ("Firewall - Private Profile", "On"), ("Firewall - Public Profile", "On"),
                ("User Account Control", "On"), ("Remote Desktop", "Disabled"), ("SMBv1 Protocol", "Disabled"),
                ("Guest Account", "Disabled"), ("Defender Signatures", "1.421.118.0 (updated 03-11-2026)"), ("Pending Restart", "No")])]),
        ],
    }


def set_field(rep, section_id, name, value, caption=None):
    for sid, _, _, _, _, blocks in rep["sections"]:
        if sid != section_id:
            continue
        for b in blocks:
            if b[0] == "fields" and (caption is None or b[1] == caption):
                for i, (n, _) in enumerate(b[2]):
                    if n == name:
                        b[2][i] = (n, value)
                        return
    raise KeyError((section_id, name))


def table(rep, section_id, caption=None):
    for sid, _, _, _, _, blocks in rep["sections"]:
        if sid == section_id:
            for b in blocks:
                if b[0] == "table" and (caption is None or b[1] == caption):
                    return b
    raise KeyError(section_id)


def set_title(rep, section_id, title):
    for i, s in enumerate(rep["sections"]):
        if s[0] == section_id:
            rep["sections"][i] = (s[0], s[1], title, s[3], s[4], s[5])


def changed_report(base):
    rep = copy.deepcopy(base)
    rep["attrs"]["generated"] = "2026-11-18T14:05:00"
    # Routine
    set_field(rep, "os", "Build Number", "26200.7019")
    set_field(rep, "os", "Last Boot", "18-11-2026 09:01")
    set_field(rep, "security", "Latest Security Update", "2026-11 Cumulative Update for Windows 11 Version 25H2 for x64-based Systems - Installed: 12-11-2026", "Patch & Update Level")
    set_field(rep, "baseline", "Defender Signatures", "1.421.118.0 (updated 03-11-2026)")
    set_field(rep, "baseline", "Pending Restart", "Yes - a restart is waiting to finish updates")
    t = table(rep, "ram")
    t[3].append(["2", "16", "Samsung", "M425R2GA3BB0-CWMOD", "5A6B7C8D", "5600", "DDR5", "DIMM 1", "BANK 0", "SODIMM"])
    set_title(rep, "ram", "RAM DETAILS - Total: 32 GB - Sticks: 2")
    t = table(rep, "storage")
    t[3][1][7] = "86"
    t = table(rep, "power")
    t[3][0][2] = "93"
    t[3][0][4] = "92990"
    t[3][0][5] = "54"
    t[3][0][6] = "Discharging"
    t = table(rep, "monitors")
    t[3].append(["DELL U2723QE", "DEL", "4291", "CN0DEMO123", "Week 14, 2025", "27\"", "DisplayPort", "Yes"])
    t = table(rep, "network")
    t[3][0][2] = "192.0.2.61"
    # Software: one updated, one removed, two added (one of them remote-access)
    sw = table(rep, "software")
    rows = [r[1:] for r in sw[3]]
    rows[0][1] = "131.0.6778.140"
    rows = [r for r in rows if not r[0].startswith("Dropbox")]
    rows.append(["AnyDesk", "9.0.4", "AnyDesk Software GmbH", "16-11-2026", "C:\\Program Files (x86)\\AnyDesk"])
    rows.append(["Python 3.13.0 (64-bit)", "3.13.150.0", "Python Software Foundation", "10-11-2026", ""])
    sw[3][:] = [[str(i + 1)] + r for i, r in enumerate(rows)]
    set_title(rep, "software", "INSTALLED SOFTWARE - Total: %d apps" % len(rows))
    ext = table(rep, "extensions")
    ext[3].append(["Chrome", "finance.user", "PDF Converter Pro", "2.1.4", "kbfnbcaeplbcioakkpcpgfkobkghlhen"])
    ext[3][1][3] = "1.61.0"
    set_title(rep, "extensions", "BROWSER EXTENSIONS - Total: 4")
    # Risky
    table(rep, "security", "Local Administrative Accounts")[3].append(["DEMO-LAPTOP-01\\support.temp", "User", "Local"])
    table(rep, "security", "Installed EDR / Antivirus")[3][0][1] = "Registered with Windows Security Center | Real-time protection: Off | Antimalware service: On | Mode: Normal"
    bl = table(rep, "security", "BitLocker Status")
    bl[3][0][1] = "Off"
    bl[3][0][2] = "FullyEncrypted"
    set_field(rep, "baseline", "Firewall - Public Profile", "Off")
    set_field(rep, "baseline", "Remote Desktop", "Enabled")
    table(rep, "drives")[3].append(["finance.user", "Z:", "Connected", "198.51.100.77", "No - Unrecognized server", "share", "\\\\198.51.100.77\\share"])
    return rep


def write(rep, path):
    root = ET.Element("DeviceDossierReport", rep["attrs"])
    for sid, num, title, state, note, blocks in rep["sections"]:
        attrs = {"id": sid, "number": num, "title": title, "state": state}
        if note:
            attrs["note"] = note
        sec = ET.SubElement(root, "Section", attrs)
        for b in blocks:
            if b[0] == "fields":
                el = ET.SubElement(sec, "Fields")
                for n, v in b[2]:
                    f = ET.SubElement(el, "Field", {"name": n})
                    f.text = v
            else:
                el = ET.SubElement(sec, "Table")
                cols = ET.SubElement(el, "Columns")
                for c in b[2]:
                    ET.SubElement(cols, "Column").text = c
                for r in b[3]:
                    row = ET.SubElement(el, "Row")
                    for c, v in zip(b[2], r):
                        cell = ET.SubElement(row, "Cell", {"column": c})
                        if v != "":
                            cell.text = v
            if b[1]:
                el.set("caption", b[1])
                el.set("captionStyle", "Bold")
    ET.indent(root, "  ")
    ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures")
    os.makedirs(out, exist_ok=True)
    base = base_report()
    write(base, os.path.join(out, "IT_Audit_DEMO-LAPTOP-01_04112026_1030.xml"))
    write(changed_report(base), os.path.join(out, "IT_Audit_DEMO-LAPTOP-01_18112026_1405.xml"))
    other = copy.deepcopy(base)
    other["attrs"]["device"] = "DEMO-DESK-07"
    set_field(other, "device", "Device Name", "DEMO-DESK-07")
    write(other, os.path.join(out, "IT_Audit_DEMO-DESK-07_05112026_0915.xml"))
    other2 = copy.deepcopy(other)
    other2["attrs"]["generated"] = "2026-11-19T09:20:00"
    write(other2, os.path.join(out, "IT_Audit_DEMO-DESK-07_19112026_0920.xml"))
    print("fixtures written to", out)


if __name__ == "__main__":
    main()
