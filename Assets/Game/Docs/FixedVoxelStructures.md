# Fixed Voxel Structures

The runtime entry point for fixed voxel structures is MinecraftCaveInfiniteWorld.

## Persistent assets

- Config/MinecraftVoxelTypes.asset stores voxel type IDs, durability, and
  rendering materials outside scene components.
- Structures/SpawnShelter.asset stores a dense, fixed scalar-and-type field.
  Every sample is applied, including air samples that carve procedural terrain.
- Scenes/VoxelStructureEditor.scene is the authoring scene. Its root
  VoxelStructureAuthoring object owns one cube child per solid sample.

In the authoring Inspector:

1. Select the voxel type and density.
2. Add, move, or delete voxel children. Child local positions are rounded to
   integer structure coordinates.
3. Use Load Structure to restore the persistent asset.
4. Use Save Structure to write the current scene field back to the asset.

Shift-clicking a voxel face adds the selected voxel to the adjacent coordinate.
Control-clicking a voxel deletes it.

## Play Mode editing

Open Scenes/VoxelStructureEditor.scene and enter Play Mode. The scene uses the
same GameHudController crosshair as InfiniteCaves and a dedicated debug camera.

- Mouse controls the view while the cursor is locked.
- WASD moves horizontally, Q/E moves vertically, and Shift accelerates.
- Left mouse removes the targeted structure voxel.
- Right mouse places the selected voxel on the targeted face.
- Escape releases or recaptures the cursor.

The edit ray reaches 48 world units, independently of player mining reach.
Changes are saved back to the assigned VoxelStructureAsset after a short delay;
Control+S forces an immediate save. Each Play session reloads the asset before
editing so saved changes remain authoritative when Play Mode objects roll back.

## Generation order

The infinite-world pipeline has explicit stages:

1. Terrain: all required procedural chunks finish their background jobs and
   commit scalar samples.
2. Structures: SpawnPointStructureRule applies the fixed field with its anchor
   aligned to the selected cave spawn voxel.
3. Meshes: only after both data passes complete are all required chunks queued
   for Marching Cubes.
4. Ready: all required mesh and collider builds have completed.

The player root Transform is the streaming viewer. During the initial build its
CharacterController is disabled and its position is held at the structure's
player spawn offset. It is placed there once more and released only when the
collision meshes are ready.

Use Tools/Supernova/Voxels/Build Fixed Structure Workflow to recreate missing
default assets, rebuild the authoring scene, and reconnect InfiniteCaves.scene.
