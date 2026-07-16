# Contributing

## Branches

- `main`: the reviewed, reproducible project baseline.
- `codex/<topic>`: short-lived feature, fix, or documentation work.
- `release/vX.Y.Z`: optional stabilization branches for tagged releases.

Keep branches short-lived and merge them into `main` through pull requests. Prefer small commits that separate project settings, source changes, and large Unity asset updates.

## Unity project hygiene

- Do not commit `Library`, `Temp`, `Logs`, `UserSettings`, generated builds, or IDE files.
- Preserve Unity `.meta` files together with their corresponding assets.
- Close Unity before performing broad asset moves or conflict resolution.
- Document the Unity, SteamVR, and Hi5 SDK versions used by a change.

## Data and third-party material

- Do not commit raw participant data, calibration exports, credentials, or machine-specific configuration.
- Do not add vendor SDK packages or binaries unless redistribution rights have been verified and documented.
- Retain all third-party copyright and license notices.

## Before opening a pull request

1. Open the project with the documented Unity version.
2. Confirm that Unity imports the project without new compile errors.
3. Run any relevant Edit Mode or Play Mode tests once tests are available.
4. Inspect `git status` and the diff for generated files, secrets, and unintended asset changes.
