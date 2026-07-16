# Findings

The experiment log. One entry per real result, newest first. Numbers with dates, not impressions.

---

## 010 — It composes: three nested timescales, three levels, each owns its own
*2026-07-16 · Predictive hierarchy · `SyntheticMind.Lab`, `FrontierTests`*

Finding 009 gave a robust two-level hierarchy. The question depth asks: does the trick *stack*? Add a third hidden cause and a third level and see whether the mechanism composes or falls apart.

**The stream** (`DeepNestedStream`): three nested timescales. A regime (frequency, ~50–350 tick dwell); a meta-regime setting how often the regime flips (~800–4000); and a meta-**meta**-regime setting how often the meta flips (~15000–25000). Same recursion at every rung — each hidden cause controls the switching *rate* of the one below and is invisible in any shorter window.

**The stack:** learned unit → `TemporalLevel` (stride 16, ~300-tick memory) → `TemporalLevel` (stride 32, integrator 0.01, ~long memory).

```
  seed:        L0 ~ regime   L1 ~ meta   L2 ~ meta-meta
  1            0.769         0.417       ~0.30
  2            0.837         0.397       ~0.30
  3            0.789         0.322       ~0.30
  min          0.769         0.322       0.296
```

### The result

**It composes. Every level owns its own timescale, on every seed.** Level 0 the fast regime (~0.8), level 1 the meta (~0.4), level 2 the meta-meta (~0.3, min 0.30 with a clock tuned for it). A slow level stacked on a slow level recovers an even-slower cause that is invisible to both levels below it — the same mechanism, applied again, one rung up.

### The one rule that makes it work

**Each level needs a clock scaled to the timescale it hunts.** Level 2 with level 1's clock barely worked (0.13–0.25); giving it a proportionally slower clock (larger stride, slower integrator) brought it to a robust 0.30. The tuning sweep was monotonic — longer level-2 memory, better meta-meta recovery — which is the signature of a real matched-filter effect, not a fluke. So depth isn't free: adding a level means picking its timescale, but there's a clear principle for picking it.

### Honest limit

**The signal fades with depth: 0.8 → 0.4 → 0.3.** Each rung estimates a rate-of-a-rate, which is inherently noisier than the thing below it, so correlation drops as you climb. It stays robustly above chance and never collapses, but this predicts a ceiling — you will not stack ten of these and still read the tenth latent at 0.3. Three works cleanly; how deep it goes before the signal is lost is unmeasured and is itself a fair question.

### Next

Depth is shown. The other half of path A — **learn on top** — is still unbuilt: a learned unit that *uses* a `TemporalLevel`'s slow representation (a readout doesn't destroy the signal the way the max-variance encoder does — finding 008). That closes the loop: fixed temporal senses producing a clean slow representation, with learning layered on top of it.

---

## 009 — Fragility solved: a robust two-timescale hierarchy
*2026-07-16 · Predictive hierarchy · `SyntheticMind.Lab`, `FrontierTests`*

Finding 008 localized the fragility to the learned max-variance encoder and identified the robust path: fixed temporal primitives (change-sensing + integration) instead of hoping the encoder discovers slowness. The choice was to take that path (build the primitives in, learn on top). This is that, built and measured.

**The piece:** `TemporalLevel` — a higher level made of fixed machinery, not a learned encoder. Each window it emits an integrated MEAN pool (slow *level* structure) concatenated with an integrated CHANGE-ENERGY pool (slow *rate* structure), so it captures the slow latent whether it's a level or a rate. It's the "built-in sense," one rung above the retina (SCAFFOLD.md §4).

**The system:** a learned unit at level 0, a `TemporalLevel` at level 1, on the nested stream.

```
  seed:        1      2      3      4      5      6      min
  L0 ~ regime 0.774  0.845  0.785  0.809  0.842  0.807  0.774   (fast timescale)
  L0 ~ meta   0.006  0.017  0.014  0.004  0.009  0.018  —       (blind, by design)
  L1 ~ meta   0.524  0.424  0.461  0.534  0.543  0.580  0.424   (slow timescale)
```

### The result

**Every seed. Both timescales. No fragility.** Level 0 robustly owns the fast regime (0.77–0.85), is blind to the slow meta-regime (0.01), and level 1 robustly owns the meta-regime (0.42–0.58). The weakest level-1 seed here (0.424) beats the *best* seed of the fragile learned version in finding 007 (0.38), and there are no failing seeds at all — where 007 failed on half.

This is a genuine, clean two-timescale hierarchy: a fast learned level that tracks the moment-to-moment cause, and a slow fixed level that tracks the hidden cause behind it, reliably, from a signal in which that cause is invisible to any short window.

### What it cost, stated plainly

Level 1 is **not learned** — it's fixed temporal machinery. So this is not "a learned hierarchy that discovers abstraction"; it's "a hierarchy where higher levels apply built-in temporal senses and could learn on top of them." That was the deliberate trade of path A: robustness now, at the price of some purity. The learned-encoder version (007) is still in the tree and still fragile — that's the honest contrast, and the seed-fragile test remains next to the robust one to keep it visible.

### What's genuinely settled

The project's central bet — that a higher level can discover structure a lower one provably cannot — is now demonstrated **robustly**, not just occasionally. Seven findings ago this was speculation; five ago it was a fragile 3/6; now it's every seed with a comfortable margin. The mechanism is understood well enough to build deliberately rather than stumble into.

### Next

The building block is solid. Options, roughly in order of how much they extend the win:
- **Depth:** a third level on a still-slower clock — does a `TemporalLevel` over a `TemporalLevel` recover an even-slower latent? Tests whether this composes.
- **Learn on top:** put a learned readout/unit above the `TemporalLevel` and have it *use* the slow representation for prediction — the "learn on top" half of path A, so far unbuilt.
- **Back to the pure question (path B), now optional:** with a robust system in hand, revisit whether a *learned* rule could replace the fixed level without the fragility. Research, not blocking.

---

## 008 — Why it's fragile, run to ground: the max-variance encoder is the bottleneck
*2026-07-16 · Predictive hierarchy · `SyntheticMind.Lab`, `FrontierTests`*

Finding 007 left the meta-regime recoverable but fragile (3/6 seeds). This is the hunt for why, by elimination. The answer turned out to be the encoder itself, and the smoking gun is unambiguous.

### The smoking gun

Feed the learned level-1 encoder a signal that *already* correlates 0.5 with the hidden meta-regime, and measure what comes out:

```
  corr(input to encoder, meta):   0.53, 0.43, 0.47   (the latent is right there)
  corr(encoder output,   meta):   0.00, 0.00, 0.00   (it comes out as nothing)
```

The max-variance (Hebbian/PCA) encoder takes a signal in which the slow latent is the *dominant* structure and outputs zero correlation with it. It chases fast, high-energy variance and structurally discards a slow, smooth latent even when handed it on a platter. **That is the whole fragility.**

### Five fixes, falsified — which is how the bottleneck got localized

| Attempt | Result | So it's not… |
|---|---|---|
| More nonlinear features (64→512) | *worse* — 512 collapses to ~0 | …feature supply (more just adds fast distractors) |
| Wider state (keep more components) | marginal (0.15→0.19) | …dropped low-variance components |
| Slowness objective at level 1 | *worse* (0.078) | …needing the *slowest* feature (meta is a *medium* timescale) |
| Slower mean-centering | no change (still 0.00) | …the encoder cancelling slow input as baseline |
| Change-energy fed to the learned encoder | 0.00 | …the input lacking the signal (it has it at 0.5) |

Every fix improved the *input* to the encoder or its settings. None helped, because the encoder is where the signal dies.

### The robust path — around the encoder, not through it

The same latent is recovered on **every** seed by two temporal primitives the architecture didn't have, used *instead of* the learned encoder:

1. **Change-sensing.** Measure how much the lower level's state moves *within* a window, not its average. Mean pooling erases the switching-rate; change-energy pooling preserves it. (`TemporalPool` gained a `ChangeEnergy` mode — and the finding is that *which summary you pool* determines *which latent survives*.)
2. **Leaky integration.** Smooth the noisy per-window change-energy over a long time constant to expose the slow mean underneath. (New `LeakyIntegrator`.)

```
  change-energy + integration vs meta:  ~0.4–0.5 on every seed  (was: 0.01 min, learned encoder)
```

Pinned in `Change_sensing_plus_integration_robustly_recovers_the_meta_regime` (all four seeds > 0.3).

### The honest headline, and the tension it creates

Fragility is **solved as detection** and **localized as learning**. We can now robustly extract the slow latent — but with *fixed* temporal primitives, not the *learned* encoder. The learned max-variance encoder cannot do it, definitively.

That sharpens the whole project to one question: **the higher-level learning rule is wrong.** Max-variance was the right first choice (it can't collapse — finding 003) but it is blind to slow structure, which is exactly what higher levels are supposed to find. This is not a tuning problem; it's the objective.

### Next

Two honest options, and they're a real fork:
- **Accept fixed temporal primitives as part of the architecture.** Biology has non-learned adaptation and integration everywhere. Higher levels could apply change-sensing + integration as built-in operations and *learn on top of* them. Pragmatic, robust, slightly less pure.
- **Replace the higher-level objective.** Find a learning rule that keeps max-variance's anti-collapse property but preferentially represents slow structure — a genuinely open research problem (proper whitened SFA, predictive-information objectives, or something new).

Either way, finding 008 is the turning point: the fragility was never in the features, the clock, or the wiring. It was the encoder, and now we know it.

---

## 007 — Stacking earns its keep — but only sometimes. The core thesis is real and fragile.
*2026-07-16 · Predictive hierarchy · `SyntheticMind.Lab`, `FrontierTests`*

The direct assault on SCAFFOLD.md §3, the one claim the project hadn't earned: does stacking make a higher level discover structure a lower level cannot? Everything before this was groundwork for this test.

**The setup.** Two new pieces:
- `NestedRegimeStream`: three nested timescales. A fast wiggle; a *regime* that flips its frequency (0.5 ↔ 1.2); and a *meta-regime* that secretly controls how OFTEN the regime flips. The meta-regime is designed to leave no trace in any short window — both frequencies occur under either meta-regime — so its only signature is the switching *rate*, visible only over a long stretch. A short-window unit is therefore blind to it by construction.
- `TemporalPool`: a slower clock. It averages the lower level's state over a window and wakes the upper level once per window, forcing level 1 to see a longer timescale than level 0. This is the ingredient finding 006 said was missing.

**The result** (correlation of each level's state with the hidden meta-regime, mean pooling, stride 32):

```
  seed:      1      2      3      4      5      6      mean
  level 0:  0.003  0.017  0.014  0.009  0.004  0.010   0.009   (blind, by construction)
  level 1:  0.378  0.039  0.087  0.071  0.331  0.338   0.207   (fragile)
```

### What this shows — genuinely, for the first time

**Level 0 is reliably blind to the meta-regime (0.009), and on several seeds level 1 recovers it (0.33–0.38).** Same stream, same meta schedule, same measurement window — the *only* difference between the two levels is temporal reach. So the recovery cannot be a leak or a windowing artifact: if it were, level 0 (which sees the identical schedule) would show it too, and it never does. The slower clock genuinely opened access to structure the lower level cannot reach. That is the core thesis demonstrated — the first time in this project a higher level has discovered something a lower one provably can't.

### What this does NOT show — and I won't pretend otherwise

**It's fragile.** Three of six seeds work (0.33–0.38); three barely do (0.04–0.09). The variance encoder isn't *seeking* the meta-regime — it's hoping its random product features happen to include one that tracks switching rate. When luck obliges, it works; when not, it doesn't. Compare finding 006, where single-unit regime detection held at 0.77–0.83 on *every* seed. This is nowhere near that solid.

**Two attempts to make it robust failed:**
- *Slowness objective at level 1* (the meta-regime is slow, so a slowness-seeker should target it): **worse**, 0.078 mean, no seed above 0.12. The meta-regime is a *medium* timescale, not the slowest thing present, so a slowness-seeker overshoots into slower noise.
- *Variance-aware pooling* (mean+variance instead of mean, to preserve switching-rate information): **worse**, collapsed to ~0. The extra channels drew the max-variance encoder onto themselves and drowned the faint meta signal.

### The honest headline

The slower clock is the right idea and it is **necessary** — without it level 1 sees nothing new. It is not yet **sufficient** for reliable abstraction. Stacking can earn its keep; making it *always* earn its keep is unsolved, and the two obvious fixes made it worse, which means the answer isn't obvious.

Pinned in tests: `Level0_is_blind_to_the_meta_regime` (robust, all seeds) and `A_slowed_level1_can_recover_what_level0_cannot` (on a known-good seed — documents possibility, not reliability).

### Next

The open problem is now sharp and specific: **reliably extract a medium-timescale latent from a pooled stream.** Threads worth pulling — an encoder objective that targets a *band* of timescales rather than max-variance (fast) or max-slowness; a predictor that must forecast many pooled steps ahead (forcing the state to hold the meta-regime); or multiple upper units at different strides so some clock is always matched to the latent. This is a real research problem, not a tuning knob — which is the most interesting place the project has reached.

---

## 006 — The win holds up: robust across seeds, and it's really reading frequency
*2026-07-16 · Predictive hierarchy v1 · `SyntheticMind.Lab`, `RegimeTests`*

Finding 005 showed a nonlinear unit recovering a hidden slow cause at correlation 0.766 — on a single seed. One seed is an anecdote. This finding stress-tests it three ways before trusting it.

### Robust across seeds

```
  seed:   1     2     3     4     5     6     7     8      mean   min
  corr:  0.766 0.831 0.787 0.824 0.787 0.820 0.829 0.802  0.806  0.766
```

Eight independent seeds (fresh stream *and* fresh random product features each time), all in a tight 0.766–0.831 band. Not a lucky draw.

### The negative control — the one that makes it trustworthy

Set both regimes to the *same* frequency. The hidden label still switches every few hundred ticks, but the observation is now identical regardless, so there is genuinely nothing to detect.

```
  seed 1: 0.003    seed 2: 0.006    seed 3: 0.005
```

Chance. This rules out the failure mode that would have quietly invalidated finding 005: a state feature that drifts slowly *for any reason* correlating with a slow-switching target just because both are slow and the window holds only ~20 switches. It doesn't. The unit scores high only when there is a real frequency difference to find.

### It degrades like a real frequency detector

Slide the two frequencies together and detection fades smoothly to chance:

```
  0.50 vs 1.20   0.766   far apart
  0.50 vs 0.80   0.831   closer
  0.50 vs 0.65   0.137   close
  0.50 vs 0.55   0.039   very close
  0.50 vs 0.50   0.007   identical (control)
```

The harder the discrimination, the lower the score — exactly the signature of genuinely reading frequency separation rather than exploiting an artifact. (0.80 edging out 1.20 is minor — likely coarser sampling of the faster oscillation; not load-bearing.)

### Status

The finding-005 capability is now hardened: **a nonlinear predictive unit reliably abstracts a hidden slow cause from a fast signal, verified across seeds and against a negative control.** Two of these three checks are now permanent tests (`The_negative_control_finds_nothing`, `Recovery_holds_on_a_different_seed`), so a future change that breaks the result — or reintroduces a leak — fails loudly.

Still open, unchanged from 005: this happens in a *single* unit with enough temporal reach. The SCAFFOLD.md §3 thesis that *stacking* produces higher, slower abstractions remains unproven. That's the next frontier — but the building block under it is now solid.

---

## 005 — A unit abstracts a hidden slow cause it was never shown (and a wrong turn along the way)
*2026-07-16 · Predictive hierarchy v1 · `SyntheticMind.Lab`, `RegimeTests`*

Finding 004 said the ball had no slow latent to discover, so no model could show abstraction on it. This finding builds the missing ingredient — a stream *with* a genuine hidden slow cause — and asks whether a unit can recover it.

**The stream** (`RegimeOscillatorStream`): a fast wiggle (sin/cos) whose frequency is secretly switched by a slow hidden "regime" every ~150–350 ticks. The regime is never an input. A frequency cannot be read from a single instant, so recovering it is genuine abstraction of an unseen cause. Success is measured by correlating a unit's state with the ground-truth regime (used for scoring only).

### The result

```
                       recovers hidden regime?
  linear unit                 corr 0.003     blind
  nonlinear unit (products)   corr 0.766     FOUND
  + slowness objective        corr 0.864     FOUND (slightly better)
```

**A single unit with nonlinear product features recovers the hidden regime** (0.77–0.86 correlation with a cause it never saw), where a linear unit gets nothing (0.003). The nonlinearity is the ingredient that matters — finding 004's instinct was right. Products across time let the encoder form frequency-sensitive features; a linear map structurally cannot, so it stays blind.

### The wrong turn — recorded because it's instructive

Mid-investigation I concluded the *opposite*: that nonlinearity didn't help and the real blocker was the encoder's objective (variance vs. slowness). That conclusion was **wrong, and it came from a confound.** The first nonlinear test ran on a 4-channel stream with two dead (always-zero) padding channels. The random product features kept landing on those dead channels, producing mostly-zero features, so the nonlinearity looked useless (corr 0.008). Cleaning the stream to 2 live channels, a controlled linear-vs-nonlinear probe settled it: 0.003 → 0.766. The nonlinearity works; the dead channels had masked it.

Lesson kept: a negative result on a confounded setup nearly overturned a correct conclusion. The isolating probe (change one thing, hold the rest) is what caught it.

### Two honest limits

1. **It's found at level 0, not up the hierarchy.** This validates "a nonlinear unit with a long enough temporal window can abstract a hidden slow cause" — a real and wanted capability. It does *not* validate the SCAFFOLD.md §3 thesis that *stacking* makes higher levels discover slower structure. The bottom unit already had the reach; level 1 added nothing here (corr ~0). That thesis is still open.
2. **The slowness objective helped only marginally (0.864 vs 0.766) and isn't necessary for this task.** It also exposed an instrument limitation: `CollapseMonitor`'s participation-ratio test gives a false "collapsed" for a slowness encoder, because slowness deliberately produces low-variance features. Anti-collapse measurement is objective-specific. The honest health check for slowness is direct: does the state carry information (recover the regime)? It does.

### Next

Two threads, either valid:
- **Push on the hierarchy thesis directly:** can stacking be made to matter — e.g., a level that pools over time (a slower clock) so level 1 *must* see longer timescales than level 0? Right now level 0 does all the work.
- **Consolidate the win:** a nonlinear unit can now abstract a hidden cause. That's a genuine building block. It may be worth making it robust before chasing depth.

---

## 004 — Stacking two learned units produces NO abstraction, and now we know why
*2026-07-16 · Predictive hierarchy v1 · `SyntheticMind.Lab`, `TimescaleMonitor`*

Findings 002 and 003 both ended on the same open question: stack two learned units — does the upper one, further from the input, discover something *slower* on its own (SCAFFOLD.md §3)? This is the first run that can actually answer it, because it's the first with an instrument for "slower": `TimescaleMonitor`, which measures lag-1 persistence (how many ticks the state holds before it substantially changes).

The test signal is chosen so the answer would be visible: a bouncing ball has *fast* structure (position, moves every tick) and *slow* structure (direction, flips only at the walls, ~30 ticks). Emergent abstraction would show as level 1 running meaningfully slower than level 0 while staying informative.

```
  level 0 (near input)       err 9.94E-004   persistence 0.997 (~288 ticks)   rank 2.00/4
  level 1 (far from input)   err 3.47E-003   persistence 0.997 (~293 ticks)   rank 2.00/4
  verdict: same speed (293 vs 288 ticks), still informative → NO emergent abstraction
```

### The result

**Level 1 runs at exactly the same timescale as level 0.** 288 vs 293 ticks is noise. Same rank, same persistence. The upper level faithfully re-represented the same signal at the same speed and discovered nothing slower. The thesis is unsupported — cleanly, measurably, not vaguely.

(Note the process: the first verdict *printed* "abstraction emerged" because the check was a bare `>` and a 2% jitter tripped it. Fixed to require a 25% margin. A negative result that needed a bug removed before it would admit it was negative — worth remembering how easily the flattering version hides.)

### Why — and this is the actual finding

**The entire stack is linear.** The encoder is linear (PCA/Sanger), the predictor is linear, the readout is linear. A linear re-encoding of a smooth signal is just another smooth signal *at the same timescale* — there is no operation anywhere in the stack that could produce a slower one.

But the ball's slow structure is **nonlinear**: "the direction flipped" is a threshold event (did position cross a wall?), not a linear combination of positions. Linear machinery cannot represent a threshold event, so it cannot extract the one slow feature that exists. The timescale is preserved because linearity *must* preserve it.

So finding 004 converts the vague "hierarchy shows nothing" (002, 003) into something sharp: **the hierarchy shows nothing because it is linear, and the structure worth abstracting is nonlinear.** That is a diagnosis, not a shrug.

### What it means for the design

Abstraction was never going to emerge from stacking, no matter how many levels, as long as every level is a linear map. The missing ingredient is a **nonlinearity** — something that lets a unit respond to an *event* ("a bounce happened") rather than only to a linear blend of its input. This is not a tuning problem; it's a missing capability.

### Next

v2: give the unit a nonlinearity, the smallest one that could let level 1 represent a threshold event. Then re-run this exact experiment — same ball, same two instruments — and see whether level 1's persistence finally pulls away from level 0's. This experiment is now the permanent measuring stick for the whole thesis.

---

## 003 — Learned state, and the collapse trapdoor, both demonstrated
*2026-07-16 · Predictive hierarchy v1 · `SyntheticMind.Lab`, `RuleTests`*

Finding 002 ended on: make the state learned, and collapse becomes live (SCAFFOLD.md §7 decision 6). v1 does exactly that — a small all-local predictive autoencoder (Hebbian/Sanger encoder + delta predictor + delta readout, no backprop) — and runs the same machine two ways, differing only in what drives the encoder.

```
  rule                              late err    ratio   state
  copy-last (baseline)             3.05E-004    0.929   rank 2.00/4
  learned/hebbian                  9.96E-004    0.028   rank 2.00/4
  learned/self-predict (fixture)   4.00E-002    0.927   rank 1.07/4   COLLAPSED
```

(Error is in INPUT space — a fixed target the model can't shrink — so it's comparable to the baselines. All 20k ticks, bouncing ball.)

### The result

**Same architecture, one decision apart, opposite outcomes.**

- **Encoder driven by variance** (Hebbian): error fell 35× (ratio 0.028), landed within 3× of copy-last, and the state stayed full (rank 2.0, the ball's true DOF). It learned a representation *and* predicted from it *and* did not collapse.
- **Encoder driven by its own predictability** (self-predict): the state caved in — rank 1.07, variance ~10⁻⁶ — and it predicts input about as badly as guessing the mean. The trapdoor is real, reachable, and not merely theoretical.

**This is the answer to decision 6, demonstrated rather than argued:** drive the encoder with something it cannot shrink to zero (variance), never with prediction error alone. It's the same lesson BYOL/SimSiam/VICReg encode in their stop-gradients and variance floors, reproduced from scratch in ~200 lines of local rules.

### A second lesson, quieter but load-bearing

The collapsed fixture predicts input at 0.04 — *bad*. Earlier intuition (002) said collapse yields deceptively *low* error. Both are true, and the difference is the grading space: collapse scores perfectly when a model is graded in **its own** state space, and terribly when graded against a **fixed** target it doesn't control. Anchoring the loss to the actual input is itself half the anti-collapse story. The readout exists for exactly this.

(Caught a real bug proving this out: the readout had no bias, so it predicted a zero-mean signal for a mean-0.5 stream — error floored at ~0.5²/2 regardless of everything else. A rate/tick sweep that moved *nothing* is what exposed it. Fixed; the numbers above are post-fix.)

### Honest limits

- Hebbian (9.96E-4) is still ~3× worse than copy-last and ~6× worse than v0's delay-line (002). **Learned state has bought robustness-to-collapse, not yet accuracy.** Its payoff is supposed to be abstraction, which needs the hierarchy —
- — and the hierarchy still shows nothing (unchanged from 002; those levels are still v0 units). The emergent-abstraction claim (SCAFFOLD.md §3) remains entirely unsupported.

### Next

Stack *learned* units and ask the 002 question again: does a second learned level, further from the input, discover something slower than the first — without being told to? That is the actual thesis, and it's the first thing v1 makes testable.

---

## 002 — The primitive works; the hierarchy doesn't yet; and the two facts are the same fact
*2026-07-15 · Predictive hierarchy v0 · `SyntheticMind.Lab`*

First run of the [SCAFFOLD.md](SCAFFOLD.md) design. One local delta rule (Widrow-Hoff, no backprop, one pass, online) vs. baselines, on a bouncing ball.

```
  rule               early err    late err    ratio   state
  running-mean       4.27E-002   4.28E-002    1.001   rank 1.59/4    COLLAPSED
  copy-last          3.29E-004   3.05E-004    0.929   rank 2.00/4
  collapsed(fixture) 3.03E-002   2.94E-002    0.969   rank 0.00/4    COLLAPSED
  linear-delta       3.14E-003   1.54E-004    0.049   rank 2.02/12
```

### What worked

- **linear-delta beat copy-last, ~2×** (1.54E-4 vs 3.05E-4). A purely local rule learned the ball's velocity. The primitive predicts.
- **The harness is honest, three ways.** On noise, running-mean scored 0.082 and copy-last 0.160 — the exact 2× that theory demands for iid data (Var[Uniform] = 0.083). Nothing beat chance on noise. The collapse detector fired on the fixture. None of these numbers were fed in; the harness reproduced them.
- **Effective rank on the ball came out 2.0** — the true degrees of freedom, measured not told.

### What didn't

Two-level hierarchy on the same ball:

```
  level 0    late err 1.54E-004   rank 2.02/12
  level 1    late err 3.09E-005   rank 2.04/36
```

Level 1's lower error is an artifact: it predicts level 0's delay line, most of which is already known at prediction time — an easier exam, not a smarter student. Its rank is still 2. **It re-represented the same two dimensions and discovered nothing slower.** The hierarchy thesis (SCAFFOLD.md §3) is unsupported so far.

### The finding under the finding

v0 cannot collapse because its state is a *fixed* function of input — nothing learned decides what to represent. But that's the identical reason it cannot abstract. **Collapse-safety and abstraction-ability are the same property with opposite signs.** You cannot get emergent hierarchy without learned state, and learned state is exactly what opens the collapse trapdoor (SCAFFOLD.md §7).

So decision 6 — what prevents collapse — is not a later problem. It is *the* problem, and it arrives the moment we make state learnable, which is v1.

### Next

v1: a rule whose state is learned, not delay-line. Predict in that learned state's space. Then watch CollapseMonitor like a hawk — the whole question is whether it finds structure without falling into the constant-state hole.

---

## 001 — The encoder ceiling is real, and we hit it on day one
*2026-07-15 · Slice 0 · `ClipEncoderTests.Report_similarity_matrix`*

[SLICE-0.md §6.5](SLICE-0.md) set finding the encoder's ceiling as the slice's most important exit criterion, and assumed it would take a hunt with a webcam and a shelf of objects. It took five stock photos and no camera at all.

### Data

Pairwise cosine similarity, CLIP ViT-B/32 vision tower, 512-d projected embeddings:

```
                 cat-a     cat-b      bear      room  landscape
 cat-a           1.000     0.533     0.537     0.524      0.399
 cat-b           0.533     1.000     0.659     0.405      0.406
 bear            0.537     0.659     1.000     0.518      0.464
 room            0.524     0.405     0.518     1.000      0.517
 landscape       0.399     0.406     0.464     0.517      1.000
```

- **cat-a** — two tabby cats on a bright pink couch, indoors
- **cat-b** — a Pallas's cat on white snow, outdoors
- **bear** — a brown bear's face, on grass
- **room** — an indoor living room
- **landscape** — a blue beach with rock formations

Also measured: same image, brightened 25% and cropped 10% → **0.935**.

### What it says

**1. The strongest off-diagonal pair in the whole matrix is cat-b ↔ bear at 0.659.** Stronger than cat-b ↔ cat-a (0.533). CLIP considers the Pallas's cat more similar to a bear than to other cats.

It isn't wrong, exactly. A Pallas's cat *is* a fluffy brown quadruped, and so is a bear. CLIP encodes what things look like, not what they are. But the practical consequence is sharp: **teach this system "my cat" from a fluffy-cat view, then show it a bear, and it will confidently say "my cat."**

**2. Relative to cat-a, another cat (0.533) and a bear (0.537) are indistinguishable.** A gap of 0.004 — well inside noise. The bear is *nominally closer*.

**3. The dynamic range is narrow.** Identical images sit at 1.000, the same image from a modestly different view at 0.935, and then *everything else in the world* compresses into 0.40–0.66. This is CLIP's known anisotropy: embeddings occupy a narrow cone rather than spreading over the sphere.

The consequence is practical and immediate: **absolute confidence thresholds are meaningless here.** Only the gap to the runner-up carries information. The CLI prints runners-up for exactly this reason, but its 0.05 "thin gap" warning is currently a guess and needs calibrating against real objects.

### What it means

**This is the empirical case for the slow system, and we now have it in hand rather than on faith.**

A frozen encoder buys instant, zero-forgetting learning — bounded by *someone else's* notion of similarity. When two things you care about fall inside that band, no amount of teaching helps, because `FastStore` only ever reads the representation. The distinction isn't there to be found. Changing the encoder is the only way through, and changing the encoder is where forgetting comes back — which is [SLICE-0.md §8](SLICE-0.md)'s Slice 3, the actual research.

**This is not a bug.** Preprocessing is verified correct — `Semantics_survive_preprocessing` shows the pink indoor cat landing closer to the snowy outdoor cat (0.533) than to the blue beach (0.399), which only happens if channel order and normalization constants are right. CLIP is working as designed. Its design simply doesn't encode the distinctions we'd want.

### Caveats — this is 5 images, not a study

- **n=5.** Suggestive, not conclusive.
- **These are different *categories*, not different *instances*.** Slice 0's real job is telling *your* mug from *your* keys — same-instance recognition. That's a different and probably easier problem than distinguishing cat species, and the 0.935 same-view number suggests instance-level separation may be much healthier.
- **The real ceiling hunt is still your desk.** Two similar mugs, two books, two faces. The prediction from this data is that visually similar *instances* will collide; that prediction is untested.

### Next

Run the CLI against ten real objects and record the same matrix. Specifically look for the two objects that collide — and if nothing collides, that's a more interesting result than if something does.
