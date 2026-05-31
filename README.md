# Illyria-Vault

A C# windows desktop app to store passwords locally.


╔══════════════════════════════════════════════════════════════════════════════╗
║                                                                              ║
║    ██╗██╗     ██╗  ██╗   ██╗██████╗ ██╗ █████╗ ███╗   ██╗                    ║
║    ██║██║     ██║  ╚██╗ ██╔╝██╔══██╗██║██╔══██╗████╗  ██║                    ║
║    ██║██║     ██║   ╚████╔╝ ██████╔╝██║███████║██╔██╗ ██║                    ║
║    ██║██║     ██║    ╚██╔╝  ██╔══██╗██║██╔══██║██║╚██╗██║                    ║
║    ██║███████╗███████╗██║   ██║  ██║██║██║  ██║██║ ╚████║                    ║
║    ╚═╝╚══════╝╚══════╝╚═╝   ╚═╝  ╚═╝╚═╝╚═╝  ╚═╝╚═╝  ╚═══╝                    ║
║                                                                              ║
║        ██╗   ██╗ █████╗ ██╗   ██╗██╗  ████████╗                              ║
║        ██║   ██║██╔══██╗██║   ██║██║  ╚══██╔══╝                              ║
║        ██║   ██║███████║██║   ██║██║     ██║                                 ║
║        ╚██╗ ██╔╝██╔══██║██║   ██║██║     ██║                                 ║
║         ╚████╔╝ ██║  ██║╚██████╔╝███████╗██║                                 ║
║          ╚═══╝  ╚═╝  ╚═╝ ╚═════╝ ╚══════╝╚═╝                                 ║
║                                                                              ║
║                 Local. Offline. Encrypted. Yours.                            ║
║                                                                              ║
╠══════════════════════════════════════════════════════════════════════════════╣
║  Version  :  1.5.5                                                           ║
║  Platform :  Windows 10 / 11  (x64)                                          ║
║  Author   :  Orest Zogju                                                     ║
║  Contact  :  orestzogju@gmail.com                                            ║
╚══════════════════════════════════════════════════════════════════════════════╝


  ──────────────────────────────────────────────────────────────────────────
   WHAT IS ILLYRIAN VAULT?
  ──────────────────────────────────────────────────────────────────────────

  Illyrian Vault is a free, offline password manager for Windows.
  Your data never leaves your device — no cloud, no accounts, no tracking.

  All passwords are protected by:
    • AES-256-GCM  field-level encryption
    • SQLCipher    full-database encryption
    • PBKDF2-SHA512  (600,000 iterations) master key derivation


  ──────────────────────────────────────────────────────────────────────────
   QUICK START
  ──────────────────────────────────────────────────────────────────────────

  1.  Run IllyrianVault.exe — no installation required.

  2.  Click "Create a Vault" and choose a strong master password.
      → Save your Recovery Key somewhere safe (printed or on a USB).
        Lose it and your password cannot be reset if you forget it.

  3.  Add entries: Logins, Cards, Notes, Identities.

  4.  Use the Generator to create strong passwords on the fly.

  5.  The vault auto-locks after 5 minutes of inactivity (adjustable
      in Settings).


  ──────────────────────────────────────────────────────────────────────────
   WHERE IS MY DATA STORED?
  ──────────────────────────────────────────────────────────────────────────

    %LOCALAPPDATA%\IllyriaVault\Profiles\<username>\vault.db

  To back up your vault, copy that folder to a USB drive or external disk.
  The vault.db file is encrypted — it is safe to store on any medium.


  ──────────────────────────────────────────────────────────────────────────
   UNINSTALLING
  ──────────────────────────────────────────────────────────────────────────

  Illyrian Vault writes nothing to the Windows Registry.
  To remove it completely:

    1. Delete  IllyrianVault.exe
    2. Delete  %LOCALAPPDATA%\IllyriaVault\   (this removes your vault data)


  ──────────────────────────────────────────────────────────────────────────
   LICENSE & COPYRIGHT
  ──────────────────────────────────────────────────────────────────────────

  Copyright (c) 2026 Orest Zogju. All Rights Reserved.
  The name "Illyrian Vault" is the exclusive intellectual property of
  Orest Zogju and may not be used in any derivative or competing product.

  This software is provided free of charge under a custom
  Source Available License (Personal, Non-Commercial Use Only).

  You may download, view, compile, and run it for personal use.
  You may NOT redistribute, sell, sublicense, or publish modified
  versions — public or private — without written permission from the author.

  Full terms: see the LICENSE file included in this package.


╔══════════════════════════════════════════════════════════════════════════════╗
║   "Your secrets, encrypted with iron — stored only on your machine."         ║
╚══════════════════════════════════════════════════════════════════════════════╝

