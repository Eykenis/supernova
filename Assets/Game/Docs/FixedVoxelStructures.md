# Fixed Voxel Structures

The runtime entry point for fixed voxel structures is MinecraftCaveInfiniteWorld.

## Persistent assets

- Config/MinecraftVoxelTypes.asset stores voxel type IDs, durability, and
  rendering materials outside scene components.
- Config/Structures/SpawnShelter.asset stores a dense, fixed scalar-and-type field.
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

For integer bulk movement, open `Tools > Supernova > Voxels > Voxel Structure
Offset` (or use `Open Box-Selection Offset Tool` on the authoring Inspector),
box-select voxel cubes in the Scene view, enter a `Voxel Offset`, and apply it
to the selection. The edit is atomic: an out-of-bounds destination or collision
with an unselected voxel rejects the complete move. `Offset All` avoids creating
a large Unity selection, while `Offset Whole Structure Via Anchor` performs an
O(1) relative placement correction by changing `Anchor` only.

## Play Mode editing

Open Scenes/VoxelStructureEditor.scene and enter Play Mode. The scene uses the
same GameHudController crosshair as InfiniteCaves and a dedicated debug camera.

- Mouse controls the view while the cursor is locked.
- WASD moves horizontally, Q/E moves vertically, and Shift accelerates.
- Left mouse removes the targeted structure voxel.
- Right mouse places the selected voxel on the targeted face.
- F5 toggles Fill Mode. In Fill Mode, left mouse selects the first targeted
  voxel and right mouse selects the second; Control+G fills their inclusive
  bounding box with the current Paint Voxel Type and Density, while Control+D
  clears every voxel in the selected box.
- Escape releases or recaptures the cursor.

The edit ray reaches 48 world units, independently of player mining reach.
Changes are saved back to the assigned VoxelStructureAsset after a short delay;
Control+S forces an immediate save. Each Play session reloads the asset before
editing so saved changes remain authoritative when Play Mode objects roll back.
The offset window remains active in Play Mode, so the Game view can place or
remove voxels while the Scene view box-selects and moves them. Successful Play
Mode offsets are saved to the assigned asset immediately.

## Generation order

The infinite-world pipeline has explicit stages:

1. Terrain: all required procedural chunks finish their background jobs and
   commit scalar samples.
2. Structures: SpawnPointStructureRule applies the fixed field with its anchor
   aligned to the selected cave spawn voxel.
3. Boundary: the top and bottom layers are restored to Bedrock after the fixed
   structure field is written.
4. Clearance: the Cell interior, its cave exit, and the Cell footprint above
   the spawn floor are carved. The vertical landing shaft
   reaches world `Y=255`, leaving an open route through the top bedrock while
   retaining bedrock outside the shaft.
5. Landing ground: a rounded safe apron is finalized around the Cell,
   independently of cave noise. It fills several samples of Stone below the
   floor and clears player headroom above it, so the first steps outside the
   pod cannot open directly into a procedural pit. Running this after the exit
   pass keeps the apron level; a passage toward a lower cave begins descending
   only after it leaves the safety margin. The guaranteed ground, clearance,
   passage, and landing-shaft cores remain unchanged, while their outer edges
   blend into the procedural density field across a Cell-scaled transition
   band (about one to two voxel samples with the current scene settings).
6. Meshes: only after all data passes complete are all required columns queued
   for Marching Cubes.
7. Ready: all required mesh and collider builds have completed.

The formal world uses X/Z-indexed `32 x 256 x 32` columns. Structure samples
outside world Y `0..255` are clipped instead of creating vertical chunks.

The player root Transform is the streaming viewer. During the initial build its
CharacterController is disabled and its position is held at the structure's
player spawn offset. It is placed there once more and released only when the
collision meshes are ready.

Use Tools/Supernova/Voxels/Build Fixed Structure Workflow to recreate missing
default assets, rebuild the authoring scene, and reconnect InfiniteCaves.scene.
