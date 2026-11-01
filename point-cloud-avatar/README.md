# PCR Avatar System

## Overview
This project implements an advanced avatar system featuring a conversational pipeline and dual control modes (AI and Human). It integrates speech processing, large language models, and facial animation capable of handling complex interaction scenarios.

## Key Features

### Conversational Pipeline
The system utilizes a fully integrated `STT -> LLM -> TTS` pipeline:
- **Speech-to-Text (STT)**: Converts user audio input to text.
- **Large Language Model (LLM)**: Generates intelligent responses based on the context.
- **Text-to-Speech (TTS)**: Converts the LLM response into audio.

### Advanced Audio Processing
- **Echo Cancellation**: Integrates WebRTC AEC3 technology to enable constant STT listening. This ensures the system does not mishear or feedback its own TTS output, allowing for seamless interruptions and natural turn-taking.

### Facial Animation
- **Audio2Face**: Utilizes Audio2Face technology to drive real-time facial animations synchronized with the TTS audio output.

## Control Modes

### 1. AI Control Mode
In this autonomous mode, the avatar represents an AI agent.
- **Animation**: Driven by a hybrid approach using a set of pre-recorded animations blended with LLM-controlled behaviors suited to the current scenario.

### 2. Human Control Mode
In this teleoperation mode, a human operator drives the avatar in real-time.
- **Upper Body & Hands**: Tracking data is received from a remote **Meta Quest** operator.
- **Lower Body**: Tracking data is captured via **Azure Kinect**.
- **Sensor Fusion**: The system fuses these data streams to create a cohesive full-body animation for the avatar.

This is the main app of the [PCR Virtual Character](../README.md) system. It runs on the participant's Meta Quest 3. The two tracking apps it receives from are in [`../point-cloud-avatar-meta-quest-upper-body-tracking/`](../point-cloud-avatar-meta-quest-upper-body-tracking/) (upper body, UDP 8889) and [`../point-cloud-avatar-kinect-body-tracking/`](../point-cloud-avatar-kinect-body-tracking/) (lower body and root, TCP 11000–11005).

## Point-Cloud Rendering

The character is captured by virtual RGB-D cameras (`DepthCamera`, `MultiCameraManager`) that render only the character layer. Depth-sensor noise is added in `DepthNoise.shader`: UV distortion, depth-dependent axial noise, quantization, and holes driven by `kinect-pattern_3x3.png` (from SimKinect, not included, see [Setup](#setup)). The noisy depth is back-projected on the GPU (`PointCloudGenerator.compute`, `Scripts/PointCloudGPU.cs`) and drawn as camera-facing billboards (`Scripts/PointCloudRendererGPU.shader`). `PCRSwitch` toggles between mesh and point-cloud rendering.

## Requirements

- Unity **6000.0.60f1** with Android Build Support. Git must be on the PATH because some packages are git UPM dependencies. The ElevenLabs package comes from the OpenUPM scoped registry in `Packages/manifest.json`.
- Meta Quest 3 (the build target is Android, min/target API 32).
- Services reachable over the network. Their code is not part of this repository:
  - **STT:** a Parakeet WebSocket server (`ParakeetSTTHandler`, `ws://<serverIP>:5092/ws/events/<id>` and `/ws/audio/<id>`). `WhisperHandler` is an alternative backend, selected in `STTHandler`.
  - **LLM:** a remote LLMUnity / llama.cpp server. Set its host on the `LLMAgent` component. The study used Qwen3-14B Q4_K_M.
  - **Audio2Face-3D:** NVIDIA's gRPC service, either local (`A2FClient` → `localHost:52000`) or NVIDIA cloud (`useCloud`, needs an NGC API key and a function ID).
  - **TTS:** ElevenLabs (cloud, needs an API key).
- For the human-controlled mode, the two tracking apps listed above.

## Setup

1. **Import the paid assets.** They are not included in this repository, and their folders are git-ignored:
   - **Final IK** (RootMotion, Unity Asset Store) → `Assets/Plugins/RootMotion/`.
   - **Azure Kinect and Femto Bolt Examples for Unity** (RF Solutions, Unity Asset Store; we used v1.21) → `Assets/AzureKinectExamples/`. The app only acts as a network client of the Kinect server, so the `SDK` folder and the sensor interfaces are not needed.

   The project **does not compile against the stock Kinect asset**. We modified these files of the asset:

   | File (under `Assets/AzureKinectExamples/`) | Change |
   |---|---|
   | `AzureKinectExamples.asmdef` | References `RootMotion` (Final IK) and `BodyTrackingStreaming` (our [`Assets/Scripts/Streaming/`](Assets/Scripts/Streaming/)). |
   | `KinectDemos/AvatarDemo/Scripts/UserAvatarMatcher.cs` | **Sensor fusion.** Drives one avatar from a primary source (Kinect `AvatarController`, `LowerBody` mask) and a secondary source (`BodyDataReceiver` from the Quest, `TopHalf` mask). Optional Final IK `BipedIK` for the legs (`useLowerBodyIK`), pelvis vertical offset, public `InitializeAvatarController()` (called by `ControlModeSwitch`). |
   | `KinectScripts/AvatarController.cs` | `BodyMask` (All / TopHalf / LowerBody), `IsTracking`, `externalLeft/RightHandRotations`, `useLowerBodyIK`, bone target position/rotation dictionaries, `keepLastPose` default on. |
   | `KinectScripts/Interfaces/NetClientInterface.cs` | Does not block the main thread while waiting for sensor data. Clears tracked bodies on disconnect. Guards reconnection. |
   | `KinectScripts/KinectInterop.cs` | Selects `memcpy` at compile time (Windows vs. other platforms), so the code runs on Android/macOS. |
   | `KinectScripts/KinectManager.cs` | Kinect4Azure / Dummy interfaces disabled; they are not available on Android/macOS. |

   We cannot redistribute the modified files. If you own a license for the asset and need them, contact the authors.
2. **Download the depth-noise pattern.** Save [`kinect-pattern_3x3.png`](https://github.com/ankurhanda/simkinect/blob/master/data/kinect-pattern_3x3.png) from SimKinect as `Assets/kinect-pattern_3x3.png` **before** opening the project, so that `Custom_DepthNoise.mat` keeps its reference. If you open the project first, assign the texture to that material by hand. The file has no declared license, so we don't redistribute it, and it is git-ignored.
3. **Open** the project in Unity 6000.0.60f1 and let the Package Manager resolve. NuGet packages (gRPC, Protobuf) are already in `Assets/Packages/` and managed by NuGetForUnity.
4. **Enter credentials and endpoints locally.** Do not commit them:
   - `A2FClient`: `apiKey` (NVIDIA NGC, `nvapi-…`; only needed with `useCloud`), `functionId`, `localHost` / `localPort`.
   - `Assets/Resources/ElevenLabsConfiguration.asset`: `apiKey`. On `ElevenLabsTTS`: `voiceId` (the study used a cloned voice of the performer, so pick your own voice).
   - `ParakeetSTTHandler`: `serverIP` / `serverPort`. `LLMAgent`: host.
   - Human-controlled mode: the Kinect server host on the `NetClientInterface` (under the `KinectManager` object). `BodyDataReceiver` listens on UDP **8889**. `TextStreamSender` uses signalling port **9000**.

   The scenes in this repository have these fields set to private lab addresses (`10.0.130.x`). Replace them with your own.
5. **Build** for Android (Meta Quest 3) via *File → Build Profiles*.

## Scenes

| Scene | Contents |
|---|---|
| `Assets/Scenes/figure.unity` | The only scene in the build list: conversation pipeline, scene switching (`SceneSessionManager`), control-mode switching, PCR switch and the WebRTC sender. |
| `Assets/Scenes/MainScene.unity` | Like `figure.unity`, without `ControlModeSwitch` / `PCRSwitch`. |
| `Assets/Scenes/AllControl.unity` | Conversation pipeline, multi-camera PCR, `ControlModeSwitch`, `PCRSwitch`. |
| `Assets/Scenes/AIControl_Exercise.unity` | Like `AllControl.unity`, plus `ExerciseController` (guided-exercise scene). |
| `Assets/Scenes/HumanControl.unity` | PCR and `BodyDataReceiver` only, without the conversation pipeline. |
| `Assets/Scenes/AIControl.unity`, `*_OldWorkingVersion.unity`, `MedicalAR Tutor.unity`, `TTM.unity` | Earlier and experimental versions. `TTM.unity` (text-to-motion) refers to an SMPL model that is not included. |

The two study environments (gym reception desk and guided exercise) are the prefabs `Assets/Prefab/AIControl_SellerScene.prefab` and `Assets/Prefab/AIControl_ExerciseScene.prefab`.

**Keyboard shortcuts (Editor):** `M` toggles mesh / PCR (`PCRSwitch`), `T` toggles AI / human control (`ControlModeSwitch`), `F5` switches scene (`SceneSessionManager`). On the Quest, `TriggerButtonComboDetector` turns Touch-controller grip + button combos into UnityEvents (wired per scene in the Inspector, including a scene-switch event) and moves the virtual scene with the thumbstick.

## Third-party content

Bundled in this repository:

| Content | Location | License |
|---|---|---|
| LLM for Unity (undream.ai) v3.0.0 | `Assets/LLMUnity/` | MIT, see `Assets/LLMUnity/LICENSE.md` |
| NuGetForUnity | `Assets/NuGet/` | MIT, see `Assets/NuGet/LICENSE` |
| gRPC, Protobuf and Microsoft.Extensions NuGet packages | `Assets/Packages/` | See the license file in each package |
| C# code generated from NVIDIA ACE Audio2Face-3D `.proto` files | `Assets/protos/` | Generated with `protoc` |
| WebRTC AEC3 native plugin for macOS and Windows (built from WebRTC's AEC3 source) | `Assets/Plugins/AEC3/` | BSD-3-Clause, see `Assets/Plugins/AEC3/LICENSE-WebRTC.txt` |
| ElevenLabs and Unity WebRTC package samples | `Assets/Samples/` | See the respective packages |
| TextMesh Pro essential resources | `Assets/TextMesh Pro/` | Unity Companion License |
| "Chip and PIN card reader" by Mixmamo.studio | `Assets/EnvironmentAssets/chip_and_pin_card_reader/` | CC-BY-4.0 ([source](https://sketchfab.com/3d-models/chip-and-pin-card-reader-ccc9d1a3f47b4537ac5b8cf75196c40c)) |
| "Convenience store 2" by Pasha | `Assets/EnvironmentAssets/convenience_store_2/` | CC-BY-4.0 ([source](https://sketchfab.com/3d-models/convenience-store-2-3e9ccca610614bcb84cb3b8390c8a9a0)) |
| "Desktop Computer" by Tyler P Halterman | `Assets/EnvironmentAssets/desktop_computer/` | CC-BY-4.0 ([source](https://sketchfab.com/3d-models/desktop-computer-561abc2fc95941609fc7bc6f232895c2)) |
| "Modular Gym" by Kristen Brown, with Matthew, Tyler and Kyle | `Assets/EnvironmentAssets/modular-gym/` | CC-BY-4.0 ([source](https://sketchfab.com/3d-models/modular-gym-86e8cc43d76c44e3925e625fdab155f3)) |
| "Store Cashier Table" by Malook Shah | `Assets/EnvironmentAssets/store-cashier-table/` | CC-BY-4.0 ([source](https://sketchfab.com/3d-models/store-cashier-table-f4a287c102db400a85f5ac510d871970)) |
| "Meta Quest 3" by Elin | `Assets/Avatar/Meta Quest 3.glb` | CC-BY-4.0 ([source](https://sketchfab.com/3d-models/meta-quest-3-65a813833dc04eeeb7d33bdca58c184c)) |

Changes to the CC-BY models: they were imported into Unity, given Unity materials and arranged in the scene prefabs. The convenience store has an added logo texture (`narvislaptopslogo*.png`). The Meta Quest 3 model was converted to a single glTF mesh with compressed textures, and the character wears it.

Not bundled. Obtain them yourself:

- Final IK and Azure Kinect and Femto Bolt Examples for Unity (see [Setup](#setup)).
- "3D Laptop Free" by Saif707 ([Sketchfab](https://sketchfab.com/3d-models/3d-laptop-free-30eea4184e9f4f6b84d89bcdf8fc83d9), Sketchfab Standard license, which does not allow redistribution). Used as a prop in `AIControl_SellerScene.prefab`; import it to `Assets/EnvironmentAssets/3d_laptop_free/`.
- The SMPL body model used by `TTM.unity` ([smpl.is.tue.mpg.de](https://smpl.is.tue.mpg.de/)).
- The Kinect speckle pattern `kinect-pattern_3x3.png` from [SimKinect](https://github.com/ankurhanda/simkinect) (see [Setup](#setup)).
- The Android (Quest) build of the AEC3 plugin, expected in `Assets/Plugins/AEC3/Android/`. It is not in this repository.
