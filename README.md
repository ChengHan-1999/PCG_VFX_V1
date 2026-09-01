# Player-Behaviour-Driven PCG for Modular VFX Texture Generation

This repository contains a Unity prototype developed for the dissertation **Player-Behaviour-Driven PCG for Modular VFX Texture Generation**. It transforms synthetic player-behaviour profiles into three modular magic-circle texture slots (weapon, boss, and region), infers a visual theme, and applies the resulting colour, particle texture, and VFX parameters in Unity.

## Software requirements

- Unity **2021.3.45f2c1** (the project was created and tested with this version)
- Windows is recommended for the recorded evaluation workflow

The repository already includes the Unity URP and Visual Effect Graph package declarations in `Packages/`. Unity Hub will restore these packages when the project is opened.

## Open the prototype

1. Clone or download this repository.
2. In Unity Hub, choose **Open** and select this `PCG_VFX_V1` folder.
3. Open the project with Unity 2021.3.45f2c1 and allow Unity to import the assets and packages.
4. Open `Assets/Scenes/SampleScene.unity` if it is not opened automatically.

## Reproduce a static player result

1. In the Hierarchy, select `VFX_MagicCircle`.
2. In the Inspector, enable **Pcg Generation Test Runner** and disable **Dynamic Trajectory Test Runner**.
3. In **Pcg Generation Test Runner**, set `Profile Id` to a player ID from `Assets/StreamingAssets/Data/PlayerProfile.json` (for example, `Player_01`).
4. Set `Seed` to **99** for the dissertation's fixed static experiment.
5. Ensure **Run On Start** is enabled, then enter Play mode.

The magic circle will display the generated weapon, boss, and region textures, followed by the inferred theme and its VFX behaviour. The Unity Console prints the candidate scores, probabilities, selected atlas indices, theme vector, theme margin, and fallback information.

## Reproduce the dynamic trajectory

1. Select `VFX_MagicCircle`.
2. Disable **Pcg Generation Test Runner** and enable **Dynamic Trajectory Test Runner**.
3. Keep `Trajectory Relative Path` as `Dynamic/DynamicPlayerTrajectory_Player01.json`, `Seed Base` as **99**, and enable **Play Trajectory In Scene**.
4. Enter Play mode.

The prototype plays the seven nodes of the Player_01 trajectory in chronological order. Each node updates the visible slots, theme, and VFX output. Node information is printed to the Unity Console.

## Main project locations

| Location | Purpose |
| --- | --- |
| `Assets/Scripts/PCG/Core/` | Scoring, sampling, dynamic trajectory processing, and theme inference |
| `Assets/Scripts/PCG/Runtime/` | Unity scene runners and visual/VFX bindings |
| `Assets/Scripts/PCG/Editor/` | CSV export and local one-at-a-time sensitivity-analysis tools |
| `Assets/StreamingAssets/Data/` | Synthetic player profiles, module definitions, theme definitions, trajectory, and algorithm configuration |
| `Assets/Textures/Atlases/` | Weapon, boss, and region texture atlases |
| `Assets/Resources/Textures/Particles/` | Runtime-loadable particle texture atlases |
| `Assets/Materials/` and `Assets/VFXGraph/` | Shader Graph, materials, and the VFX Graph asset |
| `EvaluationResults/` | Exported CSV records, sensitivity-analysis records, and final DreamSim results |
| `Tools/` | Optional scripts for DreamSim and dissertation figures |

## Optional evaluation tools

The Unity prototype can be opened and run without Python or DreamSim. DreamSim is only required for the image-perceptual-distance evaluation. Final screenshots and corresponding CSV/heatmap outputs are retained in `EvaluationCaptures/DreamSim/Static_Final/` and `EvaluationResults/DreamSim/Static_Final/`.

To regenerate the Unity CSV records, use:

`PCG VFX > Evaluation > Export All Evaluation CSV`

To rerun the local one-at-a-time parameter analysis, use:

`PCG VFX > Evaluation > Run OAT Sensitivity Analysis`

## Scope and data note

All player profiles in this prototype are synthetic, AI-assisted experimental inputs. They are not real-player telemetry and no human-participant data are included.
