---
name: pdf-3d-view-conventions
description: Use when working on how a PDF's 3D views, cameras, projection, node visibility or node names map onto a Scene — Artwork3DViewConverter, SceneView, PrcNodeNames, ResolveViews, DrawList, "empty picture", camera mirrored/upside down, parts hidden or shown wrongly, 3DV/DV/C2W/PS keys, poster vs live view.
---

# PDF 3D view conventions (learned from real files, mostly in no specification)

Each rule below is asserted by tests on real documents (`Artwork3DCameraConventionTests` needs `CALY_3D_SAMPLES=C:\caly-work\3d\samples`). Do not "correct" them from ISO 32000-2 prose.

- **Camera**: a PDF camera looks along **+Z with +X to the viewer's left** (files are right-handed whatever ISO 32000 says). `Artwork3DViewConverter` negates X and Z. Verified on 88 real views that give the camera both ways.
- **Perspective scaling `PS`** given as a number is a diameter in view-box units **only above 1**. Writers put the number **1** there (12 of 12 such sample views): read as a diameter it is a 179° lens and an empty picture; it is bound to the width.
- **Visibility is of one node only** ("the visibility of this node"): real views hide an assembly node and still mean its parts to be seen. The draw list passes nothing down the tree.
- **Default view**: `3DV` → `DV` → first view. Views are parsed lazily; malformed entries become "absent", never exceptions.
- **PRC node names**: PDF views name PRC nodes `<display name>.<identifier>`; the identifier is hexadecimal references to the PRC entity (file structure, type, number; for a solid its own then its occurrence's). Only the identifier is reliable (display names differ). Scene nodes are named the same way; `Artwork3DSceneReader.ResolveViews` / `PrcNodeNames.Resolve` **must be applied once a PRC scene exists**. `Artwork3DPrcCorpusTests` checks every named part is in the scene.
- **Units**: one unit per PRC model (`Scene.UnitScale`): first *reliable* one from the model file down (ISO 14739-1 §5.6). An occurrence's unit is **not** a scale to apply; writers put the factor in the placement.
- **Faces not shown** (behaviour bit, inherited) are left out only where a file structure's faces actually use the bit.
- **Posters**: `HasNormalAppearance` is false for ~25% of real 3D annotations (no/empty `AP`), so only the badge marks them; a poster need not picture the model (SimLab's is the background colour). A PRC silhouette check against the poster exists (`Artwork3DPosterSilhouetteTests`, needs `CALY_3D_VIEWER`).
- **Threading**: the converter reads lazily parsed PdfPig parts — call it while the page is processed, **under the document lock**.
- Activation identity is (page number, index in `PdfTextLayer.Artworks3D`), never the `PdfArtwork3D` object: text layers are evicted and rebuilt. `PdfTextLayerHelper.GetTextLayer` must not return `PdfTextLayer.Empty` when artworks exist.

When a new view/camera bug appears: find the real document, dump the raw view dictionary, compare with the poster (`tools/Scene3DViewer <file.pdf> --poster`), add the finding as a test on real data, and extend this list.
