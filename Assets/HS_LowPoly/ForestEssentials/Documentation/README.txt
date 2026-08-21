==========================================================
 HS_LP FOREST ESSENTIALS — Low Poly Forest Pack  (v1.0)
==========================================================

131 stylized low-poly forest assets: 44 unique tree species
(19 deciduous, 13 conifer, 9 character, 3 fruit/tropical),
4 saplings, water set (pond, stream, waterfall, lily pads,
reeds, water plants) and a full forest floor: rocks, logs,
bushes, grass, ferns, mushrooms, flowers and ground details.

QUICK START
-----------
1. Drag prefabs from  HS_LowPoly/ForestEssentials/Prefabs/
   into your scene. Everything is prefixed P_HS_LP_.
2. Open  Scenes/HS_LP_ForestEssentials_Demo  to browse every
   asset laid out by category.

TECHNICAL
---------
- ONE material (M_HS_LP_Palette) + one 256x224 palette
  texture (T_HS_LP_Palette, Point filter, no mips) for the
  entire pack -> minimal draw calls, great for mobile.
- ~30k triangles for ALL 131 assets (trees 300-1,200 tris,
  props 20-330 tris).
- Real-world scale, base-center pivots, clean HS_LP_ naming.
- Lightmap UVs generated on import (static-friendly).

VERTEX COLORS (texture-free workflow)
-------------------------------------
Every mesh ALSO carries vertex colors identical to the
palette. Shaders/HS_LP_VertexColor.shader (+ material
M_HS_LP_VertexColor) renders the pack with zero textures
(URP shader: main light + ambient + shadows, matte).

RENDER PIPELINES
----------------
Materials target the Universal Render Pipeline (URP) --
the default pipeline for Unity 6 projects. They use
URP/Lit with smoothness 0.
- Built-in RP: select M_HS_LP_Palette and M_HS_LP_Ground
  and switch their shader to "Standard" (the palette
  texture slots straight in; set Smoothness to 0). The
  vertex-color shader is URP-only.
- HDRP: switch the two materials to HDRP/Lit and reassign
  T_HS_LP_Palette as Base Map.
The single flat palette texture makes any conversion
lossless.

SUPPORT
-------
Questions or requests: sauravp@keybrainstech.com
