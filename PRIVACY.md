# Privacy

DriftWatch only reads the DeviceDossier XML reports you give it.

- It never connects to the PCs those reports describe, and it changes nothing on them.
- It works fully offline and sends nothing over the network.
- It keeps two small files in `%APPDATA%\DriftWatch`: `settings.json` (theme, folder watching, and the last and recent folders you opened) and `policy.json` (your rules and the changes you marked as expected). Settings has a button to open that folder and another to reset them. Turn off "Reopen the last folder" and no folder paths are kept.
- If something goes wrong, a description of the error is added to `error.log` in the same folder. It stays on your PC.
- The HTML, JSON, CSV and Markdown reports it saves contain the same kind of sensitive details as the DeviceDossier reports they were made from: device names, serial numbers, user and account names, network addresses, server paths and installed software. Store them somewhere access-controlled and share them only with people who need them.
