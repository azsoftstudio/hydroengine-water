# HydroEngine Water — product and simulation plan

**Working product name:** HydroEngine Water  
**Status:** still-water baseline; wave backends are detached prototypes, not release-ready.

## Product goal

Build a semi-realistic, game-ready URP ocean with a predictable setup, scalable visual quality, and gameplay queries that describe the surface the player sees. Target expressive adventure-game water, in the broad visual territory of *Sea of Thieves*, rather than a scientific fluid solver. A spectral ocean can be physically based on an ocean wave spectrum, but the complete real-time system will still use rendering approximations; breaking waves, spray, and interactions are not solved by FFT alone.

## User-facing choice: one active wave backend

Expose a single **Wave Model** selection on the water simulation asset/component:

1. **Gerstner** — deterministic, explicit, art-directable wave components. Use for lower cost, localized swells, stylized control, and platforms without compute support.
2. **FFT Ocean** — GPU spectral simulation driven by wind/sea-state parameters. Use for a statistically varied, natural open-ocean field and high-end close-to-mid-range ocean displacement.

The user selects one mode for the environmental wave field. Both modes share authoring concepts (wind direction, sea state/strength, wavelength bands, directional spread, global scale, time control, and quality tier) where those concepts have meaningful equivalents. Mode-specific advanced controls stay under that mode. Do not silently mix the two environmental simulations. A future, explicit **Local Swells** layer may add a small authored Gerstner contribution over an FFT ocean, but it is a separately enabled feature with defined ownership and validation; it is not the default and does not change the selected environmental backend.

“Kirschner” does not appear to be a standard ocean-wave model in the graphics references checked. This plan assumes **Gerstner** was intended. If a different named model was intended, resolve that naming before implementation.

## Frequency plan: long, middle, and short scales

Every backend must represent a useful wave spectrum across scale. A one-patch FFT or a small list of similarly sized sine waves will repeat visibly and read as a moving tile.

| Scale | Main visual role | Representation |
|---|---|---|
| Long / swell | Broad ocean elevation, horizon silhouette, slow roll | Lowest FFT cascade, or sparse long Gerstner components |
| Middle | Main visible traveling wave trains, cross-sea structure, changing slopes | Middle FFT cascades, or spectrum-sampled Gerstner components with spread directions and varied phase |
| Short / close-up | Sharp local peaks, fine slope changes, close-camera sparkle and crest breakup cues | Highest displacement-capable FFT cascade or carefully band-limited short Gerstner components; add a finer normal-only band beyond geometry sampling limits |

The short band must be **simulation-derived**, not just a pair of scrolling normal textures. However, geometry displacement cannot represent wavelengths smaller than its local vertex spacing without aliasing. Therefore the system must split short detail at a documented cutoff: simulated displacement down to the mesh/texture sampling limit; unresolved sub-grid energy contributes to slope/normal detail, not unstable vertex movement. Screen-space and mesh LOD must attenuate bands smoothly as they become undersampled.

For FFT, cascades partition wavelength/frequency ranges with smooth cross-fades and enough overlap to avoid gaps. Cascade patch sizes and resolution are chosen together, and repeat periods should differ/phase so their sum does not read as a single obvious tile. For Gerstner, generate or author components over logarithmic wavelength bins, distribute directions around the wind axis, vary phase, and cap steepness to prevent folding. Equal-weight repeated waves are not an ocean spectrum.

## Architecture boundaries

Keep responsibilities independent and connect them only through an explicit surface-state contract:

```text
Wave profile / sea state
        ↓
Selected backend (Gerstner OR FFT)
        ↓
Shared surface state
  displacement · slope/normal · velocity · compression/Jacobian · validity/time
        ├── water mesh and shading
        ├── foam/breaking cues
        ├── CPU/GPU gameplay queries
        └── optional interactions, later
```

- **HydroSurface:** mesh topology, camera-centered LOD, renderer/material binding, and surface bounds. It does not own wave equations or simulation time.
- **HydroWaveSimulation:** lifecycle and selection of exactly one backend; owns the common simulation clock and quality settings.
- **IWaveBackend:** initialize, update at a simulation rate, expose capability/validity, bind render data, and provide documented queries. Backends cannot write directly to unrelated shader properties or create their own time source.
- **Gerstner backend:** evaluate the same component set and LOD/band weights for shader displacement and exact CPU queries. Avoid a second independently authored CPU approximation.
- **FFT backend:** GPU spectrum evolution, inverse FFT, and displacement/slope outputs. GPU rendering samples those outputs. Gameplay queries use a deliberately selected strategy (CPU mirror for supported low-cost settings, or bounded asynchronous GPU readback with stated latency); never report a CPU height that does not match the rendered FFT field.
- **Surface frame:** define coordinate space, meters/world scale, sign convention, simulation timestamp, and validity. Include displacement, derivatives/normal, horizontal velocity, and compression signal only when the backend computes them consistently.
- **Rendering mesh LOD:** independent of wave-model choice. Its screen/world sampling limits drive band attenuation and set the minimum stable displacement wavelength. LOD transitions must blend the same continuous surface field on both sides of a ring.
- **Foam and lighting:** consumers of surface output, not wave generators. First derive transient crest foam from compression/steepness. Add persistent foam, shore/wake effects, scattering, and refraction only after the base wave state and controls are validated.

## Wave model details and intended limits

### Gerstner mode

Build the sea state into a deterministic set of directional components distributed across the three wavelength ranges. Keep amplitude/steepness energy bounded across all components. Provide seed/time controls for repeatable scenes and networking. Geometry displacement is directly queryable when the inversion/query method is implemented and checked; CPU and GPU must use identical parameters, weights, coordinate transforms, and time.

Gerstner is the low-cost model and an authored control path, not a claim that a handful of trochoids statistically reproduces an ocean. Avoid hiding its repeated directional pattern by merely adding more equal waves.

### FFT Ocean mode

Use a GPU Tessendorf-style spectral pipeline. Generate a directional spectrum from wind and sea-state inputs; evolve phases with gravity-wave dispersion; inverse-transform height and horizontal displacement per cascade; derive slopes/normals and horizontal-displacement derivatives; combine cascades using explicit wavelength bands. The initial spectrum implementation should be validated before introducing extra complexity. Compare a Phillips-style baseline against a tunable wind-sea spectrum (such as JONSWAP) before deciding whether a second spectrum is justified for the product.

Start profiling at modest power-of-two cascade resolutions (e.g. 128² and 256²), not as fixed promises. Measure update cost, memory, aliasing, repeat visibility, and quality on actual target hardware before creating tier labels. Simulation update rate may be lower than render rate only when time interpolation/reuse remains stable and does not desynchronize queries or foam.

FFT is periodic by construction for a finite patch. Multiple cascades reduce obvious repetition but do not magically make them non-periodic. Large-world repetition needs carefully chosen patch lengths/phases, band separation, optional stochastic sampling/blending, and a large-domain visual test.

## Execution roadmap and gates

### Phase 0 — lock the contract (in progress)

- `Runtime/Simulation/HydroWaveBackend.cs` now defines model selection, capability flags, a shared simulation step, surface sample semantics, and distinct synchronous/asynchronous query APIs. It is a contract only; it does not connect or change the current surface behavior.
- `Runtime/Simulation/HydroWaveSimulationProfile.cs` defines shared sea-state inputs and long/mid/short wavelength bands. It is the reusable shared configuration; it is not a shader-global value.
- `HydroWaveFieldDomain` maps world XZ positions to stable field meters and supports rebasing while preserving phase coordinates. Geometry rest positions and world-XZ queries must go through the same mapping.
- `Runtime/Simulation/HydroWaveSimulation.cs` now owns one ocean field's profile reference, simulation clock, and domain. Multiple mesh patches should reference the same owner; it does not instantiate a backend or change the still-water surface yet.
- Next add backend lifecycle/selection to this owner and a still backend adapter, then implement Gerstner. Keep `HydroSurface`'s current flat behavior until the selected backend can publish valid render/query data.
- Document what “accurate” means for this real-time asset: stable rendered/query agreement and plausible visual spectrum, not full Navier–Stokes or exact oceanographic prediction.
- Make a test matrix for close, mid, horizon, high/low camera angles, large domains, varying mesh density, compute unavailable, and time scaling.
- Do not reconnect the current experimental FFT bridge until it conforms to the contract.

**Gate:** the API and settings define one authoritative time, displacement space, and validity contract, plus visual/numeric comparison procedures for both backends. Phase 0 is not complete yet.

### Phase 1 — robust Gerstner backend

- Implement backend selection and the shared output contract with a still backend and Gerstner backend.
- Build spectrum-distributed components across long/mid/short bands and enforce steepness bounds.
- Match shader and CPU evaluation, including transform scale and LOD/band attenuation.
- Reconnect mesh displacement and simple instantaneous crest mask only after query and rendering agree.

**Gate:** repeatable presets produce visibly distinct, non-gridlike wave fields; query height/normal/velocity agree with the rendered surface within documented tolerances across scale and LOD boundaries.

### Phase 2 — FFT backend rebuild

- Replace the single experimental periodic patch with frequency-banded cascades and an explicit spectrum asset/sea-state workflow.
- Generate displacement and slope data; add filtering against cascade and geometry Nyquist limits.
- Set up deterministic time/seed behavior and a defined gameplay-query path.
- Profile GPU compute, memory, and render costs at each candidate resolution and update rate.

**Gate:** each cascade is inspectable in isolation; their composite has no obvious band seams or dominant tile at the test scale; query limitations and latency are honest and visible to integrations.

### Phase 3 — close-up realism and surface response

- Add the short displacement band (within sampling limits), then a higher-frequency slope/normal band for finer detail.
- Derive compression using a consistent horizontal displacement Jacobian or another validated steepness signal; tune threshold and damping against reference captures.
- Add short-lived crest foam first. Only then evaluate temporal foam persistence, spray, subsurface approximation, and underwater features as separately scoped effects.

**Gate:** close-up, mid-distance, and horizon views read as one continuous scale-rich sea; foam is sparse and tied to breaking cues rather than filling the surface.

### Phase 4 — interactions, presets, and release quality

- Add dynamic disturbances/wakes as a separate optional layer that composes into the same surface contract; specify whether each interaction affects render, queries, or both.
- Publish low/medium/high presets only after profiling representative devices and frame budgets.
- Test clean-project import for supported Unity/URP versions, fallback behavior, query examples, and scene/material migration.
- Prepare polished samples, documentation, third-party notices, truthful comparisons, and Asset Store validation.

**Gate:** reliable install/setup, documented platform limits, performance evidence, no false parity between query/render paths, and a repeatable support/debug workflow.

## Validation strategy

- **Numerical:** compare backend output at fixed positions/times against analytic Gerstner evaluation or an independent small FFT reference; verify expected wavelength and phase speed, bounded steepness, and finite normals/velocities.
- **Parity:** render displacement and query sample points in one shared coordinate/time convention; include non-unit transform scale and LOD edges. Tolerances must be chosen based on texture resolution/readback latency and stated per backend.
- **Sampling:** sweep mesh cell spacing and camera distance. Attenuate bands before aliasing; ensure denser geometry does not amplify foam, seams, or unstable normals.
- **Visual:** use a fixed neutral comparison scene with consistent camera, exposure, sky, lighting, material, and sea-state presets. Compare broad swell, mid-wave structure, short peaks, reflection, refraction, scattering, and foam separately.
- **Performance:** capture GPU/CPU time, memory, update rate, resolution, platform/GPU, and view distance. Do not use a single FPS number as a quality claim.
- **Acceptance evidence:** source-level checks plus user-run Unity compilation/play-mode captures. No editor screenshot is treated as evidence of parity without known settings and simulation time.

## Product/market notes

Crest is the closest workflow reference: it uses an FFT shape component and wave spectrum by default, with Gerstner available when users need specific authored shapes; its system combines simulation data into shared animated-wave data. KWS2 is a high-end dynamic-water competitor, while Stylized Water 2 is a simpler stylized option. Compete on clear backend choice, multiscale authoring, parity-first gameplay APIs, performance transparency, and a reliable URP experience—not unverified “more realistic” claims.

Keep the product name provisional until Asset Store, domain, social, and trademark checks. Do not imply Unity affiliation or Verified Solution status. Recheck the current Verified Solutions eligibility and Asset Store requirements before applying or submitting; approval and popularity cannot be guaranteed.

## Research references

- [Crest wave systems](https://crest.readthedocs.io/en/4.20.1/user/waves.html) — FFT environmental waves, optional authored Gerstner, spectrum authoring.
- [Crest Shape FFT](https://docs.crest.waveharmonic.com/Components/Inputs/ShapeFFT.html) — FFT spectrum, resolution/repetition controls, attenuation, and collision baking.
- [Crest system notes](https://docs.crest.waveharmonic.com/Manual/Guides/SystemNotes.html) — frequency-appropriate LODs, combined data, geometry and normal LOD blending.
- [Tessendorf, Simulating Ocean Water](https://jtessen.people.clemson.edu/reports/papers_files/waterslides2001.pdf) — foundational spectral ocean method.
- [Unity Northstar Ocean System Design](https://github.com/oculus-samples/Unity-NorthStar/blob/main/Documentation/OceanSystemDesignAndImplementation.md) — practical Unity iFFT implementation and resolution/minimum-wave-size considerations.
- [Unity HDRP Water system](https://unity.com/blog/engine-platform/new-hdrp-water-system-in-2022-lts-and-2023-1) — frequency range, FFT-generated waves, and deterministic simulation considerations.
- [UnityOcean technical walkthrough](https://antoniospg.github.io/UnityOcean/OceanSimulation.html) — horizontal displacement, slope-derived normals, and folding/Jacobian foam signals.
- [Sea of Thieves: The Technical Art of Sea of Thieves](https://history.siggraph.org/wp-content/uploads/2022/09/2018-Talks-Ang_The-Technical-Art-of-Sea-of-Thieves.pdf) — art-directed real-time ocean rendering reference.
- [Unity Asset Store submission guidelines](https://marketplace.unity.com/publishing/submission-guidelines) — recheck before release.
- Competitor listings to recheck: [Crest Water 5](https://assetstore.unity.com/packages/tools/particles-effects/crest-water-5-oceans-rivers-lakes-268614), [KWS2](https://assetstore.unity.com/packages/tools/particles-effects/kws2-dynamic-water-system-323662), and [Stylized Water 2](https://assetstore.unity.com/packages/vfx/shaders/stylized-water-2-for-unity-2021-2023-170386).
