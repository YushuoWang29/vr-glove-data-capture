# VR Glove Data Capture

A Unity-based research project for capturing human hand-motion data with an HTC VIVE headset and Noitom Hi5 2.0 data gloves through SteamVR.

基于 Unity、HTC VIVE 与诺亦腾 Hi5 2.0 数据手套的人手活动数据采集项目。

## Project status

This repository is in early development. The initial revision contains the Unity project scaffold and the SteamVR Unity plugin. Application-specific acquisition, calibration, synchronization, and export modules will be developed in later revisions.

The proprietary Hi5 SDK, vendor demo builds, generated Unity caches, and captured participant data are intentionally not distributed in this repository.

## Hardware and software

- HTC VIVE Pro 2
- Noitom Hi5 2.0 data gloves
- SteamVR
- Unity `6000.5.2f1`
- Hi5 2.0 SteamVR Headset Unity SDK `1.1.0.23` (obtain separately from the vendor)

## Repository layout

```text
Unity Project/
  Hi5_2/
    Assets/           Unity assets and the vendored SteamVR plugin
    Packages/         Unity package manifest and lock file
    ProjectSettings/  Version-controlled Unity project settings
```

Unity-generated `Library`, `Temp`, `Logs`, `UserSettings`, and build directories are excluded from version control. External SDK distributions, standalone vendor samples, archives, and raw capture data are also excluded.

## Getting started

1. Clone this repository.
2. Install Unity `6000.5.2f1` through Unity Hub.
3. Install SteamVR and verify that the headset is tracked correctly.
4. Obtain the Hi5 2.0 SteamVR Headset Unity SDK `1.1.0.23` from its authorized distribution channel.
5. Follow the vendor documentation when importing or configuring the Hi5 SDK; do not commit the proprietary SDK package or generated build output.
6. Open `Unity Project/Hi5_2` in Unity.

The current revision does not yet provide a complete data-acquisition workflow.

## Data policy

Raw recordings, participant data, calibration exports, and generated datasets must not be committed to Git. Store them in an approved research-data location and commit only schemas, synthetic examples, or de-identified samples that have been explicitly cleared for publication.

## Development workflow

The default branch is `main`. Future development should use short-lived branches such as `feature/<name>`, `fix/<name>`, and `docs/<name>`, with changes merged through pull requests. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Licensing

Original project code is licensed under the [MIT License](LICENSE). Third-party components retain their original licenses and copyright notices; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the files under `LICENSES/`.
