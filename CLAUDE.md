# Repository Guidelines

## Project Structure & Module Organization

This is a Unity/Tuanjie 2022.3 project. Keep first-party work under `Assets/Game`: runtime C# is organized by feature in `Runtime/`, editor tooling belongs in `Editor/`, and EditMode tests live in `Tests/Editor/`. Game-owned prefabs, configuration, animations, structures, scenes, and design notes sit in their matching `Assets/Game/` folders. The player-facing flow starts in `Assets/Scenes/Home.scene`, loads missions through `Assets/Scenes/DenseJigsawRegion.scene`, and exposes `Assets/Scenes/SpawnShelterStoneTest.scene` as the tutorial; `InfiniteCaves.scene` is a disabled reference scene. Shared materials, prefabs, URP settings, and UI assets are in the top-level `Assets/` folders. Treat `Assets/3rd/` and vendored `Packages/` content as external code.

## Build, Test, and Development Commands

Use editor version `2022.3.62t11` (Tuanjie `1.9.3`) from `ProjectSettings/ProjectVersion.txt`.

- Open the repository through Unity Hub/Tuanjie Hub for normal development and Play Mode checks.
- `"<Editor>" -batchmode -quit -projectPath . -runTests -testPlatform EditMode -testResults Logs/EditMode.xml` runs the automated suite.
- `"<Editor>" -batchmode -quit -projectPath . -buildWindows64Player Builds/Windows/Supernova.exe` creates a Windows build.

Before building, confirm Build Settings enables `Home.scene`, `DenseJigsawRegion.scene`, and `SpawnShelterStoneTest.scene` in the intended order. Do not commit generated `Library/`, `Temp/`, `Logs/`, or `UserSettings/` content.

## Coding Style & Naming Conventions

Follow the existing C# style: four-space indentation, braces on their own lines, and one public type per matching file. Use `PascalCase` for types, methods, properties, and constants; use `camelCase` for parameters, locals, and private fields. Place code in feature namespaces such as `Supernova.Voxels` or `Supernova.MinecraftCaves.Editor`. Runtime code must not reference `UnityEditor`; keep editor-only APIs below an `Editor/` directory.

## Testing Guidelines

Tests use Unity Test Framework `1.1.33` and NUnit. Name fixtures `*Tests.cs` and test methods after observable behavior, for example `ViewerMovement_RefreshesStreaming`. Add focused EditMode coverage for pure logic and asset/scene wiring. Run the full suite before submitting; if a documented baseline failure remains, distinguish it from regressions in the PR.

## Assets, Commits, and Pull Requests

Move or rename Unity assets with their `.meta` files intact, preferably inside the editor. The history currently contains only `Initial Commit`, so no detailed commit convention is established; use short, imperative subjects such as `Fix voxel streaming refresh`. PRs should describe scope and player impact, link relevant issues, list test results, and include screenshots or clips for scene, UI, animation, or rendering changes. Call out package, project-setting, prefab, scene, and `.meta` changes explicitly.

## 要求

禁止使用硬编码路径，应查找全局路径表，并更新该表以适配路径加载.

禁止 git stash，未明确时，git 只允许 add remove commit push pull
