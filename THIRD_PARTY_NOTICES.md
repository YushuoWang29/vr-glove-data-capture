# Third-party notices

The repository-level MIT License applies only to original project material for which the project authors hold the copyright. Third-party components remain subject to their own license terms.

## SteamVR Unity Plugin

- Copyright holder: Valve Corporation
- Location: `Unity Project/vr-glove-data-capture/Assets/SteamVR`
- License: BSD 3-Clause License
- License text: [`LICENSES/SteamVR-BSD-3-Clause.txt`](LICENSES/SteamVR-BSD-3-Clause.txt)

The SteamVR copyright notice, license conditions, and disclaimer must be retained when redistributing its source or binary forms. Valve's name and contributor names may not be used to endorse or promote derived products without prior written permission.

## Unity Recorder

- Copyright holder: Unity Technologies ApS
- Package: `com.unity.recorder@2.2.0-preview.4`
- Location after Package Manager restore: `Library/PackageCache/com.unity.recorder@2.2.0-preview.4`
- License: Unity Companion License for Unity-dependent projects

The repository tracks only the Package Manager dependency declaration and lock data. Unity Package Manager restores the package locally; the package remains subject to Unity's license and is not relicensed under this repository's MIT License.

## Hi5 2.0 SDK

The Hi5 2.0 SDK, vendor documentation, packages, and demo builds are not distributed by this repository. Users must obtain them from an authorized source and comply with the vendor's applicable terms. Local imports under `Assets/NoitomHi5` and `Assets/Hi5_Interaction_SDK` are intentionally excluded from Git.

## YCB Object and Model Set

- Dataset authors: Berk Calli, Arjun Singh, Aaron Walsman, Siddhartha Srinivasa, Pieter Abbeel, and Aaron M. Dollar
- Included subset: `005_tomato_soup_can`, `025_mug`, and `055_baseball`, using the Google 16k textured meshes
- Location: `Unity Project/vr-glove-data-capture/Assets/VRGloveDataCapture/Resources/RoboticsObjects/YCB`
- Source: [The YCB Object and Model Set](https://ycb-benchmarks.s3-website-us-east-1.amazonaws.com/)
- License: Creative Commons Attribution 4.0 International (CC BY 4.0)
- License notice: [`LICENSES/YCB-CC-BY-4.0.txt`](LICENSES/YCB-CC-BY-4.0.txt)

The files retain their dataset object identifiers and are redistributed for robotic manipulation research. Runtime scale normalization, simplified Unity collision primitives, and placement logic are project-authored adaptations; the mesh geometry and textures are not relicensed under the repository MIT License.
