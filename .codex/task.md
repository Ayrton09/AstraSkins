# StatTrak music-kit tracking

- Objective: bind persisted per-player music-kit MVP counts to the controller's `m_iMusicKitMVPs` state.
- Status: implementation and verification complete.
- Key files: `SkinManager.cs`, `SqliteSkinStorage.cs`, `MySqlSkinStorage.cs`, `AstraSkinsPlugin.cs`, `Models/PlayerSkinProfile.cs`.
- Verification: `dotnet build -c Release --no-restore`, `git diff --check`, and a SQLite storage round-trip test passed.
- Artifact: `AstraSkins-stattrak-fix.zip`.
- Retro: the original implementation wrote counters but never loaded or applied them; the async profile merge also needed additive handling for MVPs recorded before a load completed.
