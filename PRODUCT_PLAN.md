# HydroEngine Water — product plan

**Working product name:** HydroEngine Water  
**Proposed Asset Store title:** HydroEngine Water — URP Water & Simulation  
**Status:** cleaned prototype baseline; wave systems are detached from the default surface. This is not an Asset Store-ready package.

## Product direction

Build a practical URP water system whose rendered surface, foam, and gameplay queries all consume one defined simulation output. The differentiator should be a clear setup workflow and trustworthy integration for gameplay teams—not an unverified claim to replace Crest or KWS.

Market review previously found established products serving different needs: Crest covers broad water use cases and multiple render pipelines; KWS2 markets high-end simulation and interactions; Stylized Water 2 serves accessible stylized water. Their features, prices, and popularity change, so recheck live listings before setting launch pricing or making comparative claims. Compete first on setup, predictable APIs, clear quality tiers, and good examples.

Keep the name provisional until searching the Asset Store, web, domains, social handles, and trademark databases. Avoid implying Unity affiliation or endorsement.

## Current code boundaries

- **`HydroSurface`:** mesh generation, camera-follow, mesh LOD, water material assignment, and still-plane gameplay queries. It does not run a wave simulation.
- **Gerstner authoring:** `Runtime/Simulation/Gerstner/HydroWaveProfile.cs` stores and generates wave-profile data. It is intentionally not connected to the current surface renderer or query path.
- **Spectral simulation:** `Runtime/Simulation/Spectral/` contains the experimental GPU FFT backend and a separate renderer bridge. It can be attached explicitly for evaluation; it is not part of the default surface lifecycle.
- **Foam:** persistent foam history has been removed. The shader retains basic shoreline foam and a compression-driven crest mask for a future wave backend.
- **Gameplay:** buoyancy and the height follower query the still plane. They must not be enabled with the experimental spectral renderer, whose CPU query contract currently returns `false`.

This split is deliberate groundwork. The FFT implementation is still experimental: one periodic patch, no cascades, no shared query data, and no measured quality/performance profile. Do not treat it as a production mode.

## Implementation roadmap

### 1. Stable still-water baseline — current

- Keep `HydroSurface` small and responsible for mesh/material/lifecycle only.
- Keep the water mesh still by default.
- Keep wave authoring and the experimental FFT code in separate simulation folders.
- Keep basic shader foam; defer foam history until the wave signal is stable.
- Remove transient debug UI/gizmos from the product-facing Inspector.

### 2. Define the simulation contract

Create a backend interface and a shared surface-frame contract before reconnecting displacement:

```text
simulation backend -> displacement, normals, velocity, compression
                  -> renderer
                  -> foam
                  -> gameplay queries
```

The renderer must not invent wave data independently from the query or foam system. Establish ownership for time, world scale, LOD filtering, and coordinate spaces. Add a still backend and Gerstner backend first; preserve Gerstner as the low-cost deterministic choice.

### 3. Rebuild spectral ocean as a controlled backend

- Validate and correct discrete spectrum energy scaling and FFT normalization.
- Split swell, wind-chop, and fine-wave frequency bands into explicit cascades.
- Blend cascade displacement and normals across distance/LOD; measure repetition in a large test domain. FFT patches are periodic and are not automatically tile-free.
- Add GPU cost/update-rate controls and graceful compute-platform fallback.
- Keep the current FFT renderer bridge disabled by default until the rebuilt backend passes visual and numeric checks.

### 4. Reunify gameplay and rendering

Choose a query strategy for GPU displacement: a CPU spectral approximation, bounded asynchronous GPU readback with clearly defined latency, or an explicitly unsupported gameplay-query tier. Compare query height, normal, and velocity against the actual rendered surface before enabling buoyancy. Never silently return Gerstner-only samples when FFT geometry is visible.

### 5. Add interactions and foam

After the base backend and query contract are stable, add an optional bounded dynamic-wave layer for wakes and disturbances. Feed its output into the same surface frame. Reintroduce short-lived foam history only when compression/shoreline signals are validated. Jacobian compression is a useful crest indicator, not a complete breaking-wave simulation.

### 6. Quality tiers and release

Define low/medium/high presets only after profiling named GPUs, resolutions, simulation rates, and camera distances. Compare Gerstner and FFT in the same lighting/camera scene. Test package import in clean URP projects at the supported Unity versions, document dependencies, include a polished sample, and run Unity Asset Store validation before submission.

## Commercial and verification plan

- Publish accurate compatibility, dependency, limitations, and license information.
- Include user documentation, a functional demo, third-party notices, clean metadata, and truthful images/video.
- Avoid claims of “top,” “verified,” or Unity endorsement. Verified Solutions is a separate program with eligibility requirements; recheck Unity’s current criteria before applying. Asset Store approval and popularity cannot be guaranteed.
- Use compatible licenses for all third-party code/assets and preserve required notices. The current prototype does not vendor reference-repository code.

## Research references

- [Crest system notes](https://docs.crest.waveharmonic.com/Manual/Guides/SystemNotes.html) — camera-centered LOD and wave-data responsibilities.
- [Crest wave systems](https://crest.readthedocs.io/en/4.20.1/user/waves.html) — environmental and authored wave paths.
- [Crest foam](https://docs.crest.waveharmonic.com/Manual/Appearance/Foam.html) — crest foam generation and decay.
- [Sea of Thieves, SIGGRAPH 2018](https://history.siggraph.org/wp-content/uploads/2022/09/2018-Talks-Ang_The-Technical-Art-of-Sea-of-Thieves.pdf) — FFT-driven ocean appearance, compression/foam signals, and scattering approximations.
- [Tessendorf, Simulating Ocean Water](https://jtessen.people.clemson.edu/reports/papers_files/coursenotes2004.pdf) — foundational spectral ocean formulation.
- [NVIDIA GPU Gems: Effective Water Simulation](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models) — directional/Gerstner wave construction and mesh-frequency considerations.
- [Unity Asset Store submission guidelines](https://marketplace.unity.com/publishing/submission-guidelines) — recheck before submission; policy changes over time.
- Competitor listings to recheck: [Crest Water 5](https://assetstore.unity.com/packages/tools/particles-effects/crest-water-5-oceans-rivers-lakes-268614), [KWS2](https://assetstore.unity.com/packages/tools/particles-effects/kws2-dynamic-water-system-323662), and [Stylized Water 2](https://assetstore.unity.com/packages/vfx/shaders/stylized-water-2-for-unity-2021-2023-170386).
