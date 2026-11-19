using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DriftWatch.Core
{
    /// <summary>
    /// Security-first scoring. Each raw difference gets a severity, a category, a plain-English headline and a
    /// short note on why it matters. Rules look at the direction of a change, so "Off → On" for the firewall is
    /// good news (Info) while "On → Off" is High.
    /// </summary>
    public static class RiskRules
    {
        // Remote-access, tunnelling, torrent and hacking tools. Legitimate in some offices, so they are flagged
        // for a human to confirm rather than treated as malware.
        private static readonly string[] RemoteAccess = { "anydesk", "teamviewer", "rustdesk", "ultraviewer", "splashtop", "logmein", "screenconnect", "connectwise control", "supremo", "radmin", "realvnc", "tightvnc", "ultravnc", "tigervnc", "remote utilities", "dwservice", "parsec", "chrome remote desktop", "ammyy" };
        private static readonly string[] Tunnels = { "ngrok", "tor browser", "cloudflared", "zerotier", "tailscale", "hamachi", "softether", "psiphon" };
        private static readonly string[] Unwanted = { "utorrent", "bittorrent", "qbittorrent", "vuze", "frostwire", "limewire", "kmspico", "kms auto", "kmsauto", "crack", "keygen", "mimikatz", "cain & abel", "hashcat", "john the ripper", "lazagne", "psexec", "nirsoft" };
        private static readonly string[] DualUse = { "wireshark", "npcap", "nmap", "zenmap", "advanced ip scanner", "angry ip scanner", "putty", "winscp", "filezilla", "netcat", "process hacker", "cheat engine" };
        private static readonly string[] SecurityProducts = { "defender", "antivirus", "anti-virus", "endpoint", "eset", "kaspersky", "bitdefender", "sophos", "crowdstrike", "falcon", "sentinelone", "sentinel agent", "malwarebytes", "norton", "mcafee", "trend micro", "avast", "avg ", "webroot", "carbon black", "cylance", "cortex xdr", "wazuh", "sysmon", "huntress", "f-secure", "withsecure", "panda", "comodo", "emsisoft", "gdata", "g data", "fortinet", "forticlient" };

        /// <summary>Classifies a program name: "Remote access", "Tunnelling", "Unwanted", "Admin tool", "Security" or "".</summary>
        public static string SoftwareRisk(string name)
        {
            string lower = (name ?? "").ToLowerInvariant();
            if (Matches(lower, RemoteAccess)) return "Remote access";
            if (Matches(lower, Tunnels)) return "Tunnelling";
            if (Matches(lower, Unwanted)) return "Unwanted";
            if (Matches(lower, DualUse)) return "Admin tool";
            if (Matches(lower, SecurityProducts)) return "Security";
            return "";
        }

        public static void Classify(Change c, Snapshot baseline, Snapshot current)
        {
            string id = (c.SectionId ?? "").ToLowerInvariant();
            string table = (c.Table ?? "").ToLowerInvariant();
            Set(c, Severity.Info, CategoryFor(id), DefaultTitle(c), "");

            if (c.Item == "Section status") { SectionStatus(c); return; }
            switch (id)
            {
                case "security":
                    if (table.Contains("antivirus")) Antivirus(c);
                    else if (table.Contains("bitlocker")) BitLocker(c);
                    else if (table.Contains("administrative")) Admins(c);
                    else if (table.Contains("patch")) Patch(c);
                    break;
                case "baseline": Baseline(c, current); break;
                case "drives": MappedDrives(c); break;
                case "software": Software(c); break;
                case "extensions": Extensions(c); break;
                case "device": DeviceInfo(c); break;
                case "motherboard": Motherboard(c); break;
                case "os": Os(c); break;
                case "network": Network(c); break;
                case "storage": Storage(c); break;
                case "ram": Memory(c); break;
                case "cpu": Cpu(c); break;
                case "power": Power(c); break;
                case "gpu": Gpu(c); break;
                case "monitors": Monitors(c); break;
                case "asset": Set(c, Severity.Info, "Asset", "Asset record updated: " + c.Item, "The asset details entered at audit time changed. Check the inventory matches."); break;
            }
        }

        private static void SectionStatus(Change c)
        {
            string after = c.After.ToLowerInvariant();
            if (after.StartsWith("error") || after.StartsWith("skipped"))
                Set(c, Severity.Medium, "Audit", "Audit coverage dropped for " + c.SectionTitle, "This section could not be fully read in the newer audit, so changes in it may be hidden. Re-run DeviceDossier as administrator.");
            else if (after.StartsWith("warning"))
                Set(c, Severity.Low, "Audit", "Partial results for " + c.SectionTitle, "Some details in this section could not be read in the newer audit.");
            else
                Set(c, Severity.Info, "Audit", c.SectionTitle + " read in full again", "This section was incomplete before and is complete now.");
        }

        private static void Antivirus(Change c)
        {
            if (c.Kind == ChangeKind.Removed) { Set(c, Severity.Critical, "Security", "Antivirus or EDR removed: " + c.Item, "A security product that protected this PC is no longer registered. Confirm it was replaced on purpose, not disabled or uninstalled by malware or a user."); return; }
            if (c.Kind == ChangeKind.Added) { Set(c, Severity.Low, "Security", "Antivirus or EDR added: " + c.Item, "A new security product is registered. Check it is approved and not conflicting with the existing one."); return; }
            if (TurnedOff(c.Before, c.After, "Real-time protection:")) { Set(c, Severity.Critical, "Security", "Real-time protection turned off", c.Item + " is no longer scanning files as they open. This is a common first step in an attack. Turn it back on and find out who changed it."); return; }
            if (TurnedOff(c.Before, c.After, "Antimalware service:")) { Set(c, Severity.Critical, "Security", "Antimalware service stopped", c.Item + "'s service is off, so the PC has no active malware protection."); return; }
            if (TextUtil.Contains(c.After, "Mode: Passive") && !TextUtil.Contains(c.Before, "Mode: Passive")) { Set(c, Severity.High, "Security", "Defender switched to passive mode", "Defender only runs passively when another antivirus takes over. Check that another product is installed and active."); return; }
            if (TurnedOn(c.Before, c.After, "Real-time protection:")) { Set(c, Severity.Info, "Security", "Real-time protection turned back on", "Good news: file scanning is active again."); return; }
            Set(c, Severity.Low, "Security", "Antivirus status changed: " + c.Item, "");
        }

        private static void BitLocker(Change c)
        {
            if (c.Kind == ChangeKind.Removed) { Set(c, Severity.Medium, "Security", "BitLocker no longer reports drive " + c.Item, "This drive is missing from BitLocker's list. It may have been removed, or encryption may no longer apply to it."); return; }
            if (c.Kind == ChangeKind.Added) { Set(c, Severity.Low, "Security", "BitLocker now reports drive " + c.Item, "A new drive appears in BitLocker's list. Check that it is encrypted."); return; }
            string f = c.Field.ToLowerInvariant();
            if (f.Contains("protection"))
            {
                if (StartsWith(c.Before, "On") && !StartsWith(c.After, "On")) { Set(c, Severity.Critical, "Security", "BitLocker protection turned off on " + c.Item, "Data on this drive can now be read if the PC is lost or stolen. BitLocker is often suspended for BIOS or Windows updates, but it should be resumed straight after."); return; }
                if (!StartsWith(c.Before, "On") && StartsWith(c.After, "On")) { Set(c, Severity.Info, "Security", "BitLocker protection turned on for " + c.Item, "Good news: this drive is now protected."); return; }
            }
            if (f.Contains("volume"))
            {
                if (TextUtil.Contains(c.After, "Decrypt")) { Set(c, Severity.Critical, "Security", "Drive " + c.Item + " is being decrypted", "Someone is removing BitLocker encryption from this drive. Confirm this was approved."); return; }
                if (TextUtil.Contains(c.After, "FullyEncrypted")) { Set(c, Severity.Info, "Security", "Drive " + c.Item + " is fully encrypted", "Good news: encryption has finished."); return; }
                Set(c, Severity.Low, "Security", "BitLocker status changed on " + c.Item, ""); return;
            }
            if (f.Contains("encryption %"))
            {
                var a = TextUtil.Number(c.Before); var b = TextUtil.Number(c.After);
                if (a.HasValue && b.HasValue && b < a) { Set(c, Severity.High, "Security", "Encryption dropped on " + c.Item + " (" + c.Before + " → " + c.After + ")", "Less of this drive is encrypted than before, which usually means decryption is in progress."); return; }
                Set(c, Severity.Info, "Security", "Encryption progress on " + c.Item, ""); return;
            }
            if (f.Contains("protector"))
            {
                bool lostTpm = TextUtil.Contains(c.Before, "Tpm") && !TextUtil.Contains(c.After, "Tpm");
                bool lostRecovery = TextUtil.Contains(c.Before, "RecoveryPassword") && !TextUtil.Contains(c.After, "RecoveryPassword");
                Set(c, lostTpm || lostRecovery ? Severity.Medium : Severity.Low, "Security", "BitLocker key protectors changed on " + c.Item,
                    lostRecovery ? "The recovery password protector was removed. Make sure a recovery key is still stored safely." : "The ways this drive can be unlocked have changed. Check it was deliberate.");
            }
        }

        private static void Admins(Change c)
        {
            if (c.Kind == ChangeKind.Added) Set(c, Severity.Critical, "Accounts", "New local administrator: " + c.Item, "This account can now install software, turn off security tools and access every file on the PC. Confirm who added it and why. Attackers often create admin accounts to keep access.");
            else if (c.Kind == ChangeKind.Removed) Set(c, Severity.Medium, "Accounts", "Local administrator removed: " + c.Item, "This account no longer has admin rights. Usually good housekeeping, but check that whoever looks after this PC can still sign in as an admin.");
            else Set(c, Severity.Medium, "Accounts", "Administrator account details changed: " + c.Item, "");
        }

        private static void Patch(Change c)
        {
            if (c.Field.IndexOf("Security", StringComparison.OrdinalIgnoreCase) >= 0)
                Set(c, Severity.Info, "Security", "New security update installed", "Good news: Windows installed a newer security update.");
            else
                Set(c, Severity.Info, "Security", "Update history changed: " + c.Field, "");
        }

        private static void Baseline(Change c, Snapshot current)
        {
            string f = c.Field;
            if (f.StartsWith("Firewall", StringComparison.OrdinalIgnoreCase))
            {
                string profile = f.Replace("Firewall - ", "").Replace(" Profile", "").ToLowerInvariant();
                if (StartsWith(c.Before, "On") && StartsWith(c.After, "Off")) { Set(c, profile == "public" ? Severity.Critical : Severity.High, "Security", "Firewall turned off (" + profile + " networks)", profile == "public" ? "The PC is unprotected on public Wi-Fi such as cafés and airports, where attacks are most likely. Turn the firewall back on." : "Network traffic on " + profile + " networks is no longer filtered. Turn the firewall back on unless another firewall replaces it."); return; }
                if (StartsWith(c.Before, "Off") && StartsWith(c.After, "On")) { Set(c, Severity.Info, "Security", "Firewall turned on (" + profile + " networks)", "Good news."); return; }
                Set(c, Severity.Low, "Security", "Firewall setting changed (" + profile + " networks)", ""); return;
            }
            switch (f)
            {
                case "Secure Boot":
                    if (StartsWith(c.Before, "On") && !StartsWith(c.After, "On")) Set(c, Severity.High, "Security", "Secure Boot turned off", "Without Secure Boot, malicious software can load before Windows starts. Turn it back on in the BIOS unless there is a documented reason.");
                    else if (StartsWith(c.After, "On")) Set(c, Severity.Info, "Security", "Secure Boot turned on", "Good news.");
                    return;
                case "User Account Control":
                    if (StartsWith(c.Before, "On") && StartsWith(c.After, "Off")) Set(c, Severity.High, "Security", "User Account Control turned off", "Programs can now make system changes without asking. Turn UAC back on.");
                    else Set(c, Severity.Info, "Security", "User Account Control turned on", "Good news.");
                    return;
                case "Remote Desktop":
                    if (StartsWith(c.After, "Enabled")) Set(c, Severity.High, "Security", "Remote Desktop turned on", "The PC now accepts Remote Desktop connections, a frequent target for password-guessing attacks. Make sure it is needed, limited to the office network or VPN, and protected by strong passwords.");
                    else Set(c, Severity.Info, "Security", "Remote Desktop turned off", "Good news: one less way into the PC.");
                    return;
                case "SMBv1 Protocol":
                    if (TextUtil.Contains(c.After, "Enabled") && !TextUtil.Contains(c.After, "Disabled")) Set(c, Severity.High, "Security", "SMBv1 turned on", "SMBv1 is an outdated file-sharing protocol used by ransomware such as WannaCry. Turn it off unless an old device truly needs it.");
                    else Set(c, Severity.Info, "Security", "SMBv1 setting changed", "");
                    return;
                case "Guest Account":
                    if (TextUtil.Contains(c.After, "Enabled") && !TextUtil.Contains(c.After, "Disabled")) Set(c, Severity.High, "Security", "Guest account turned on", "Anyone can now sign in without a password. Turn the Guest account off.");
                    else Set(c, Severity.Info, "Security", "Guest account turned off", "Good news.");
                    return;
                case "TPM":
                    Set(c, Severity.Medium, "Security", "TPM status changed", "The security chip that BitLocker and Windows Hello rely on reports differently. Check it is still enabled and activated.");
                    return;
                case "Firmware Type":
                    Set(c, Severity.Medium, "Security", "Firmware type changed", "The PC switched between UEFI and legacy BIOS mode, which affects Secure Boot and BitLocker.");
                    return;
                case "Pending Restart":
                    if (StartsWith(c.After, "Yes")) Set(c, Severity.Low, "Security", "Restart needed to finish updates", "Updates are installed but not active until the PC restarts.");
                    else Set(c, Severity.Info, "Security", "Pending restart cleared", "");
                    return;
                case "Defender Signatures":
                    Signatures(c, current);
                    return;
            }
            Set(c, Severity.Low, "Security", "Security setting changed: " + f, "");
        }

        private static void Signatures(Change c, Snapshot current)
        {
            DateTime updated;
            int open = c.After.IndexOf("updated ", StringComparison.OrdinalIgnoreCase);
            if (open >= 0 && current.Generated != DateTime.MinValue &&
                DateTime.TryParseExact(c.After.Substring(open + 8).TrimEnd(')', ' '), new[] { "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out updated))
            {
                double age = (current.Generated - updated).TotalDays;
                if (age > 7) { Set(c, Severity.Medium, "Security", "Defender signatures are " + (int)age + " days old", "Malware definitions should update daily. Check Windows Update and Defender can reach the internet."); return; }
            }
            Set(c, Severity.Info, "Security", "Defender signatures updated", "");
        }

        private static void MappedDrives(Change c)
        {
            string known = Cell(c, "Matches Known Server List");
            if (c.Kind == ChangeKind.Added)
            {
                if (StartsWith(known, "No")) Set(c, Severity.High, "Network", "Drive mapped to an unrecognised server: " + c.Item, "This server is not on your known server list. It could be a personal NAS, a home share or an attacker-controlled machine used to copy data. Confirm what it is.");
                else Set(c, Severity.Low, "Network", "New mapped drive: " + c.Item, "Points to a known server.");
                return;
            }
            if (c.Kind == ChangeKind.Removed) { Set(c, Severity.Info, "Network", "Mapped drive removed: " + c.Item, ""); return; }
            if (c.Field.IndexOf("Known Server", StringComparison.OrdinalIgnoreCase) >= 0 && StartsWith(c.After, "No"))
            { Set(c, Severity.High, "Network", "Mapped drive now points to an unrecognised server: " + c.Item, "This drive letter used to point to a known server."); return; }
            if (c.Field.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0 || c.Field.IndexOf("UNC", StringComparison.OrdinalIgnoreCase) >= 0)
            { Set(c, Severity.Medium, "Network", "Mapped drive target changed: " + c.Item, "Check the new location is approved."); return; }
            Set(c, Severity.Info, "Network", "Mapped drive details changed: " + c.Item, "");
        }

        private static void Software(Change c)
        {
            string name = Cell(c, "Software Name");
            if (name.Length == 0) name = c.Item;
            string lower = name.ToLowerInvariant();
            if (c.Kind == ChangeKind.Added)
            {
                if (Matches(lower, RemoteAccess)) { Set(c, Severity.High, "Software", "Remote-access software installed: " + c.Item, "This lets someone control the PC from elsewhere. It is also a favourite of scammers and attackers. Confirm IT approved it."); return; }
                if (Matches(lower, Tunnels)) { Set(c, Severity.High, "Software", "Tunnelling or anonymising software installed: " + c.Item, "This can bypass the office network's controls or hide traffic. Confirm it is approved."); return; }
                if (Matches(lower, Unwanted)) { Set(c, Severity.High, "Software", "Potentially unwanted software installed: " + c.Item, "Torrent clients, licence cracks and password tools are common sources of malware and legal risk."); return; }
                if (Matches(lower, DualUse)) { Set(c, Severity.Medium, "Software", "Admin or network tool installed: " + c.Item, "Useful for IT staff, but also used by attackers to explore a network. Check who needs it."); return; }
                if (Matches(lower, SecurityProducts)) { Set(c, Severity.Low, "Software", "Security software installed: " + c.Item, ""); return; }
                Set(c, Severity.Low, "Software", "New software: " + c.Item, "Check it is licensed and approved.");
                return;
            }
            if (c.Kind == ChangeKind.Removed)
            {
                if (Matches(lower, SecurityProducts)) { Set(c, Severity.High, "Software", "Security software removed: " + c.Item, "Check this PC is still protected and that the removal was planned."); return; }
                Set(c, Severity.Low, "Software", "Software removed: " + c.Item, "");
                return;
            }
            if (c.Field.Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                int cmp = CompareVersions(c.Before, c.After);
                if (cmp > 0) { Set(c, Severity.Medium, "Software", "Software downgraded: " + name + " (" + c.Before + " → " + c.After + ")", "An older version may bring back fixed security holes. Check why it was rolled back."); return; }
                Set(c, Severity.Info, "Software", "Updated: " + name + " (" + c.Before + " → " + c.After + ")", "");
                return;
            }
            Set(c, Severity.Info, "Software", "Software details changed: " + name, "");
        }

        private static void Extensions(Change c)
        {
            if (c.Kind == ChangeKind.Added) Set(c, Severity.Low, "Software", "New browser extension: " + c.Item, "Extensions can read the pages a user visits. Check it comes from a trusted publisher.");
            else if (c.Kind == ChangeKind.Removed) Set(c, Severity.Info, "Software", "Browser extension removed: " + c.Item, "");
            else Set(c, Severity.Info, "Software", "Extension updated: " + c.Item + " (" + c.Before + " → " + c.After + ")", "");
        }

        private static void DeviceInfo(Change c)
        {
            switch (c.Field)
            {
                case "Domain / Workgroup":
                    Set(c, Severity.High, "System", "Domain or workgroup changed", "The PC joined or left a domain. This changes who controls it and which policies apply."); return;
                case "Device Name":
                    Set(c, Severity.Medium, "System", "Device renamed", "Update your inventory, and check the rename was planned."); return;
                case "BIOS Serial Number":
                case "System UUID":
                case "Manufacturer":
                case "Model":
                    Set(c, Severity.High, "Hardware", "Hardware identity changed: " + c.Field, "The serial or model no longer matches. The motherboard may have been replaced, or this may be a different PC reported under the same name."); return;
                case "BIOS Version":
                case "BIOS Release Date":
                    Set(c, Severity.Low, "Hardware", "BIOS updated (" + c.Field + ")", "Firmware updates are normally good. Check BitLocker and Secure Boot are still on afterwards."); return;
            }
            Set(c, Severity.Info, "Hardware", "Device detail changed: " + c.Field, "");
        }

        private static void Motherboard(Change c)
        {
            if (c.Field.IndexOf("Serial", StringComparison.OrdinalIgnoreCase) >= 0 || c.Field.IndexOf("Product", StringComparison.OrdinalIgnoreCase) >= 0)
                Set(c, Severity.High, "Hardware", "Motherboard replaced", "The motherboard's identity changed. Check this repair was recorded.");
            else
                Set(c, Severity.Info, "Hardware", "Motherboard detail changed: " + c.Field, "");
        }

        private static void Os(Change c)
        {
            switch (c.Field)
            {
                case "Install Date":
                    Set(c, Severity.High, "System", "Windows was reinstalled", "A fresh install wipes security settings and installed tools. Check the PC was set up again to your standard."); return;
                case "OS Name":
                    Set(c, Severity.Medium, "System", "Windows edition changed", "A change such as Pro to Home removes BitLocker and policy support."); return;
                case "Activation Status":
                    if (TextUtil.Contains(c.Before, "Licensed") && !TextUtil.Contains(c.After, "Licensed")) Set(c, Severity.Medium, "System", "Windows is no longer activated", "An unactivated copy can be a sign of a changed or unlicensed key.");
                    else Set(c, Severity.Info, "System", "Windows activation changed", "");
                    return;
                case "Windows Key":
                case "Product Key Ch.":
                    Set(c, Severity.Medium, "System", "Windows product key changed", "A different key is in use. Check it comes from your licence pool."); return;
                case "Build Number":
                case "Version":
                case "Display Version":
                    if (CompareVersions(c.Before, c.After) > 0) Set(c, Severity.Medium, "System", "Windows rolled back (" + c.Before + " → " + c.After + ")", "An update was removed, which can reopen fixed security holes.");
                    else Set(c, Severity.Info, "System", "Windows updated (" + c.Field + ": " + c.Before + " → " + c.After + ")", "");
                    return;
            }
            Set(c, Severity.Info, "System", "Windows detail changed: " + c.Field, "");
        }

        private static void Network(Change c)
        {
            string adapter = c.Item.ToLowerInvariant();
            bool virtualish = adapter.Contains("vpn") || adapter.Contains("tap") || adapter.Contains("wintun") || adapter.Contains("wireguard") || adapter.Contains("tunnel") || adapter.Contains("zerotier") || adapter.Contains("hamachi");
            if (c.Kind == ChangeKind.Added) { Set(c, virtualish ? Severity.Medium : Severity.Low, "Network", (virtualish ? "New VPN or tunnel adapter: " : "New network adapter: ") + c.Item, virtualish ? "Check this VPN is approved. Personal VPNs can bypass office security controls." : ""); return; }
            if (c.Kind == ChangeKind.Removed) { Set(c, Severity.Info, "Network", "Network adapter removed: " + c.Item, ""); return; }
            switch (c.Field)
            {
                case "DNS":
                    Set(c, Severity.Medium, "Network", "DNS servers changed on " + c.Item, "DNS decides which website a name like your bank's points to. Unexpected DNS changes can redirect users to fake sites."); return;
                case "Gateway":
                    Set(c, Severity.Low, "Network", "Default gateway changed on " + c.Item, "Normal when moving between networks."); return;
                case "MAC Address":
                    Set(c, Severity.Low, "Network", "MAC address changed on " + c.Item, "Can mean the adapter was replaced, or the address is being randomised."); return;
                case "DHCP":
                    Set(c, Severity.Low, "Network", "DHCP setting changed on " + c.Item, ""); return;
            }
            Set(c, Severity.Info, "Network", "Network address changed on " + c.Item + " (" + c.Field + ")", "Normal for laptops that move between networks.");
        }

        private static void Storage(Change c)
        {
            if (c.Kind == ChangeKind.Added) { Set(c, Severity.Medium, "Hardware", "New drive installed: " + c.Item, "Record it in the inventory and make sure it is encrypted."); return; }
            if (c.Kind == ChangeKind.Removed) { Set(c, Severity.High, "Hardware", "Drive removed: " + c.Item, "A drive that held data is gone. Confirm it was wiped or stored securely, and not taken."); return; }
            if (c.Field.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var a = TextUtil.Number(c.Before); var b = TextUtil.Number(c.After);
                if (a.HasValue && b.HasValue && b < a)
                {
                    double drop = a.Value - b.Value;
                    if (b < 80) { Set(c, Severity.High, "Hardware", "Drive health low: " + c.Item + " (" + c.Before + "% → " + c.After + "%)", "This drive is wearing out. Check backups and plan a replacement."); return; }
                    if (drop >= 10) { Set(c, Severity.Medium, "Hardware", "Drive health falling fast: " + c.Item + " (" + c.Before + "% → " + c.After + "%)", "A drop of " + drop.ToString("0", CultureInfo.InvariantCulture) + " points between audits. Watch it closely and make sure it is backed up."); return; }
                    Set(c, Severity.Low, "Hardware", "Drive health down slightly: " + c.Item, ""); return;
                }
            }
            if (c.Field.IndexOf("Firmware", StringComparison.OrdinalIgnoreCase) >= 0) { Set(c, Severity.Info, "Hardware", "Drive firmware updated: " + c.Item, ""); return; }
            Set(c, Severity.Info, "Hardware", "Drive detail changed: " + c.Item + " (" + c.Field + ")", "");
        }

        private static void Memory(Change c)
        {
            if (c.Kind == ChangeKind.Added) Set(c, Severity.Medium, "Hardware", "Memory added: " + c.Item, "Update the inventory. Unrecorded hardware changes can mean parts were swapped.");
            else if (c.Kind == ChangeKind.Removed) Set(c, Severity.Medium, "Hardware", "Memory removed: " + c.Item, "A memory module is gone. Confirm this was a recorded repair or upgrade.");
            else if (c.Field.IndexOf("Serial", StringComparison.OrdinalIgnoreCase) >= 0 || c.Field.IndexOf("Capacity", StringComparison.OrdinalIgnoreCase) >= 0)
                Set(c, Severity.Medium, "Hardware", "Memory module replaced: " + c.Item, "The module in this slot is different.");
            else Set(c, Severity.Info, "Hardware", "Memory detail changed: " + c.Item, "");
        }

        private static void Cpu(Change c)
        {
            if (c.Kind == ChangeKind.Changed && c.Field.IndexOf("Max Speed", StringComparison.OrdinalIgnoreCase) >= 0) { Set(c, Severity.Info, "Hardware", "CPU speed reading changed", ""); return; }
            Set(c, Severity.Medium, "Hardware", "Processor changed: " + c.Item, "The CPU's identity is different. This usually means a motherboard or CPU replacement.");
        }

        private static void Power(Change c)
        {
            if (c.Field.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var a = TextUtil.Number(c.Before); var b = TextUtil.Number(c.After);
                if (a.HasValue && b.HasValue && a - b >= 10) { Set(c, Severity.Medium, "Hardware", "Battery health falling fast (" + c.Before + "% → " + c.After + "%)", "Plan a battery replacement."); return; }
                Set(c, Severity.Info, "Hardware", "Battery health " + c.Before + "% → " + c.After + "%", ""); return;
            }
            if (c.Kind != ChangeKind.Changed) { Set(c, Severity.Low, "Hardware", "Battery or PSU " + c.KindText.ToLowerInvariant() + ": " + c.Item, ""); return; }
            Set(c, Severity.Info, "Hardware", "Battery or PSU detail changed (" + c.Field + ")", "");
        }

        private static void Gpu(Change c)
        {
            if (c.Kind != ChangeKind.Changed) Set(c, Severity.Low, "Hardware", "Graphics card " + c.KindText.ToLowerInvariant() + ": " + c.Item, "");
            else if (c.Field.IndexOf("Driver", StringComparison.OrdinalIgnoreCase) >= 0) Set(c, Severity.Info, "Hardware", "Graphics driver updated: " + c.Item, "");
            else Set(c, Severity.Info, "Hardware", "Display setting changed: " + c.Item + " (" + c.Field + ")", "");
        }

        private static void Monitors(Change c)
        {
            if (c.Kind == ChangeKind.Added) Set(c, Severity.Info, "Hardware", "Monitor connected: " + c.Item, "");
            else if (c.Kind == ChangeKind.Removed) Set(c, Severity.Info, "Hardware", "Monitor no longer connected: " + c.Item, "");
            else Set(c, Severity.Info, "Hardware", "Monitor detail changed: " + c.Item, "");
        }


        private static void Set(Change c, Severity s, string category, string title, string why)
        {
            c.Severity = s;
            c.Category = category;
            c.Title = title;
            c.Why = why ?? "";
        }

        private static string CategoryFor(string id)
        {
            switch (id)
            {
                case "security": case "baseline": return "Security";
                case "software": case "extensions": return "Software";
                case "network": case "drives": return "Network";
                case "os": return "System";
                case "asset": return "Asset";
                default: return "Hardware";
            }
        }

        private static string DefaultTitle(Change c)
        {
            if (c.Kind == ChangeKind.Added) return "Added: " + c.Item;
            if (c.Kind == ChangeKind.Removed) return "Removed: " + c.Item;
            return c.Item == c.Field || c.Field.Length == 0 ? c.Item + " changed" : c.Item + " · " + c.Field + " changed";
        }

        private static string Cell(Change c, string column)
        {
            string v;
            return c.Row != null && c.Row.TryGetValue(column, out v) ? v ?? "" : "";
        }

        private static bool Matches(string lower, string[] list)
        {
            return list.Any(w => lower.Contains(w));
        }

        private static bool StartsWith(string v, string prefix)
        {
            return (v ?? "").TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string After(string text, string label)
        {
            int i = (text ?? "").IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            string rest = text.Substring(i + label.Length).TrimStart();
            int bar = rest.IndexOf('|');
            return (bar >= 0 ? rest.Substring(0, bar) : rest).Trim();
        }

        private static bool TurnedOff(string before, string after, string label)
        {
            return StartsWith(After(before, label), "On") && StartsWith(After(after, label), "Off");
        }

        private static bool TurnedOn(string before, string after, string label)
        {
            return StartsWith(After(before, label), "Off") && StartsWith(After(after, label), "On");
        }

        /// <summary>Compares dotted versions numerically. Returns 1 when <paramref name="a"/> is newer than <paramref name="b"/>.</summary>
        public static int CompareVersions(string a, string b)
        {
            var pa = Parts(a); var pb = Parts(b);
            if (pa.Count == 0 || pb.Count == 0) return 0;
            for (int i = 0; i < Math.Max(pa.Count, pb.Count); i++)
            {
                long x = i < pa.Count ? pa[i] : 0, y = i < pb.Count ? pb[i] : 0;
                if (x != y) return x > y ? 1 : -1;
            }
            return 0;
        }

        private static List<long> Parts(string v)
        {
            var result = new List<long>();
            foreach (var part in (v ?? "").Split('.', '-', '_', ' '))
            {
                long n;
                string digits = new string(part.TakeWhile(char.IsDigit).ToArray());
                if (digits.Length == 0) { if (result.Count > 0) break; else continue; }
                if (long.TryParse(digits, out n)) result.Add(n);
            }
            return result;
        }
    }
}
