# HydroEngine Water

HydroEngine is an early URP water prototype. It is not yet an Asset Store release.

## Current baseline

`HydroSurface` owns the water mesh, transform-follow behavior, bounded/camera-centered LOD, and a flat-plane query. The default surface is intentionally still: wave simulation is not coupled to the mesh controller. Debug gizmos, CPU-query overlays, setup-validation text, and persistent foam history have been removed.

The shader provides water color, depth absorption, opaque-scene refraction, Fresnel/environment lighting, optional planar/SSR reflections, and simple shoreline/compression foam. Compression foam remains dormant on a still surface until a wave backend supplies compression.

## Wave systems kept separate

- `Runtime/Simulation/Gerstner/HydroWaveProfile.cs` contains editable wave-profile data and deterministic spectrum authoring. It is retained for the future Gerstner backend, but is not currently connected to `HydroSurface`.
- `Runtime/Simulation/Spectral/` contains the experimental GPU spectral simulation, compute shader, and `HydroSpectralOceanRenderer` bridge. Add that component to a `HydroSurface` to opt into its single repeating FFT patch. It is detached from the default surface behavior and is not production-ready; cascades, query parity, and dynamic disturbances remain future work.
- While the spectral renderer is active, CPU height/surface/velocity queries return `false` instead of reporting the still plane as if it matched the rendered waves. The Inspector retains a warning for this state. `HydroBuoyancy` therefore cannot float correctly in this experimental mode yet.

## Quick start

1. Add **GameObject > Water > HydroEngine Water Surface**.
2. Assign a material using **HydroEngine/Water (URP)**. The surface Inspector can create one.
3. Leave `HydroSpectralOceanRenderer` unattached unless you are specifically evaluating the experimental backend.
4. `SampleHeight` returns the still plane height for gameplay. `HydroBuoyancy` and `HydroHeightFollower` use that query.

## Samples and scope

Use **Tools > HydroEngine > Generate Water Demo Scene** for a still-water scene with a shoreline, reflections, and buoyancy objects. The old scratch `Test` scene has been removed; `Generated/HydroWaveProfile.asset` remains as standalone Gerstner authoring data.

URP is the only supported render pipeline. Camera depth and opaque textures are needed for depth-based appearance and refraction. Underwater rendering, wave simulation on the default surface, wake interactions, mobile presets, non-URP support, and release performance measurements are not included. See [PRODUCT_PLAN.md](PRODUCT_PLAN.md) for the staged system architecture and release gates; [CHANGELOG.md](CHANGELOG.md) records the current cleanup.
