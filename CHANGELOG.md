# HydroEngine Water — development changelog

## Unreleased — clean simulation baseline

- Simplified `HydroSurface` to mesh generation, LOD, material assignment, and still-plane queries.
- Detached Gerstner profile authoring and the experimental GPU spectral renderer into `Runtime/Simulation/` subfolders. Neither drives the default surface; the water is still unless the spectral renderer is explicitly attached.
- Removed Scene-view wave-vector/query gizmos, buoyancy probe visualization, CPU-query overlays, and the text-heavy active setup validation Inspector.
- Kept the Inspector warning that explains spectral mode returns `false` for height/velocity queries rather than pretending its GPU surface matches a different CPU surface.
- Removed persistent crest-foam history scripts, render shader, material controls, and scene-builder setup. The water shader retains only basic shoreline/compression foam.
- Removed the scratch `Test` scene/assets and the now-unused duplicate demo wave profile. Kept one standalone generated Gerstner profile for future simulation work.
- Cleaned the README and product plan to describe the current still baseline and staged simulation architecture accurately.

## 0.1.0 — prototype foundation

- Added the URP water shader and procedural water mesh with bounded and camera-centered LOD layouts.
- Added planar reflections, environment reflections, screen-space reflection, depth absorption/refraction, buoyancy, and a gameplay surface-query API.
- Added Gerstner wave-profile authoring and an experimental single-patch GPU spectral FFT prototype. The latter remains isolated from the default surface and is not a release-ready ocean backend.
