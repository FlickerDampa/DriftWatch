# Changelog

## 1.0.0-beta.1

First public beta.

**Monitoring**
- Open a folder of DeviceDossier XML reports (subfolders included). Reports are grouped by PC and kept in date order.
- Watches the folder and re-checks when a new audit arrives, with an in-app message and an optional Windows notification.
- Overview of the fleet: security score, PCs that need attention, overdue audits, score trend, open changes and the weakest controls.

**Security score**
- Every audit is scored out of 100 against 20 checks covering antivirus, encryption, firewall, remote access, accounts, updates, platform security and software.
- Grades A to F, advice for each failing check, and a compliance matrix across all PCs.

**Comparisons**
- Compare any two audits of a PC and list every change, riskiest first, across five levels (Critical, High, Medium, Low, Info), with a plain-English note on why each one matters.
- Rows are matched by identity (program name, account, drive letter, serial number), not position.
- Mark changes as expected so they stop counting towards verdicts.

**Software and policy**
- Fleet software inventory from each PC's latest audit, with remote-access, tunnelling, torrent and admin tools flagged.
- Policy for banned, approved and required software, approved admin accounts and thresholds. Ban, approve or require a program from the Software page. Import and export as JSON.

**Reports and automation**
- Self-contained HTML reports (single PC and fleet), JSON, CSV and a Markdown summary for tickets.
- Command line with `--posture`, `--policy`, `--min-score` and exit codes for scheduled tasks and RMM tools.

**App**
- Dark and light themes that follow Windows, sharp at any display scaling, keyboard shortcuts and Ctrl+K search.
