# Findings

The experiment log. One entry per real result, newest first. Numbers with dates, not impressions.

---

## 037 — object → word: the plumbing works, a real hit exists, but faces swamp it
*2026-07-21 · `SyntheticMind.Name` · the summit — reached partway, honestly*

The hardest step, toward NAMES: bind a discrete *object* (a salient sub-region, not the whole scene) to a discrete *word* (a voiced run cut from continuous speech, not a whole clip). New fixed front-ends — `ObjectAttention` (a saliency fovea) and `WordSegmenter` (energy VAD, tractable because child-directed speech isolates words with pauses) — feed the same cross-situational PMI binder.

**It works as plumbing, and on 2 videos it produced a genuine hit:** attention isolated a red **fire truck** on its own and bound it to a word 13× (PMI 2.61). That is exactly the target — an object, not a scene, tied to a word. Proof the mechanism *can* do the thing.

**But at full-corpus scale it's swamped by faces.** 106,737 co-occurrences, 64 object + 48 word units, and the top pairings by PMI are all the presenter's face:
```
  word #20 <-> object #42 (Ms Rachel's face)   pmi 1.75, 11x
  word #37 <-> object #46 (Ms Rachel's face)   pmi 1.74,  6x
  word #10 <-> object #9  (Ms Rachel's face)   pmi 1.62,  7x
```

### Why PMI didn't rescue it this time (the real lesson)

In see→say, one *single* ever-present sound dominated, so PMI discounted that one unit and the distinctive scenes surfaced. Faces don't behave that way: attention lands on the face constantly, but the face **fragments into many object-units** (different framings, expressions, headband colours). No single face-unit co-occurs with *everything*, so none gets discounted — instead each face-framing pairs at moderate PMI with the common words said in it. Fragmentation defeats the PMI trick. **Saliency ≠ objecthood, and faces win the saliency vote**, so the object side is mostly faces and the real objects (the fire truck) are buried.

### Honest limits

- **This is the first capability that didn't work at scale.** The loop is correct and the front-ends are individually sound (tested), but the *result* on the full corpus is face-dominated, not object→word. The 2-video fire-truck hit is real but not representative of the rankings.
- **The bottleneck is attention, not binding.** PMI is doing its job; it's being fed faces. Better objects in → better words out.
- Word units unverified by ear here; the object failure dominates the story regardless.

### Next — this is where breadth should turn to depth

- **Novelty attention instead of saliency**: attend to what *appeared/changed* rather than what merely stands out. A held-up object is novel; the presenter's face is always there. This directly targets the face problem — the face is high-saliency but low-novelty.
- Or explicit face-suppression (skin-tone / persistent-region down-weighting).
- This is the honest place the project's own reflection pointed to: stop adding loops, make *one* front-end (attention) actually good, so object→word stops being "a real hit buried in faces" and becomes solid.

---

## 036 — see → say: a picture makes it speak, grounded end to end
*2026-07-21 · `SyntheticMind.SeeSay` · full corpus (28 videos) · the loop closed*

The capstone. Every branch built over the last stretch, joined into one path: **show it a picture and it speaks.** It perceives the scene with the same colour retina + video codebook it learned by watching (finding 032), recalls the sound-unit that scene was *bound* to across the corpus (the cross-situational PMI pairings, reverse-looked-up with `HeardForSeen`), and reproduces that sound with the vocal tract it taught itself by babbling (findings 034/035). **No labels anywhere in the entire chain** — not in perceiving the scene, not in knowing its sound, not in producing it.

Proven on 2 videos first (loop correct, but recall undiscriminative — everything fell back to one ever-present "talking" sound). On the **full 28-video corpus**, PMI finally has the cross-video recurrence it needs:

```
  46 scenes perceived and spoken. Standouts:
  saw scene-unit 24 (a YELLOW SCHOOL BUS)  -> recalled sound-unit 13 (bound 28x, PMI 1.25) -> said it (58% closer)
  saw scene-unit 80 (outdoor, person)      -> recalled sound-unit 12 (bound 51x)           -> said it (60% closer)
  saw scene-unit 24/54 (bus-like)          -> both recalled sound-unit 13                  (consistent)
```
The school-bus result is the one that matters: a **distinctive sight recalling a distinctive sound** (almost certainly the "Wheels on the Bus" audio), with two different bus-ish scene-units both landing on the same sound. It saw a bus and said the bus's sound — grounded, unsupervised, end to end.

### Honest limits

- **A common speech sound still wins for generic scenes.** Several non-distinctive scenes (23, 73, 70) all recalled sound-unit #0 — the residual "presenter talking" sound. PMI *discounts* the ever-present sound but doesn't eliminate it; a scene with no distinctive sound of its own falls back to the common one. The bus works because it has *both* a distinctive look and a distinctive sound.
- **Still-image perception has no motion.** The retina's motion channel is blank for a single frame (the units were learned with motion), a slight mismatch — scenes still map sensibly, but it's a caveat.
- **"Said" is a vowel approximation.** A held vowel aimed at the recalled sound's averaged timbre — not the sound over time, not intelligible speech. The vowel-only tract and static-timbre limits from findings 034/035 carry through.
- **Scene → sound, not object → word.** It recalls "the sound that goes with buses," not the word "bus". Grounding is at the level of whole audiovisual context.
- Recall uses the nearest real exemplar clip as the recalled sound; imitation quality varies with how vowel-like that clip is.

So: a complete, honest, unsupervised **see → say** loop. It is not language, and it says so — but a machine that can look at a picture, recall from its own experience what that picture sounds like, and make that sound with a voice it invented by babbling, having been told *nothing*, is the whole thesis of the project standing on its own two feet.

### Next

- **Suppress the ever-present sound** at recall (e.g. skip units that pair with almost everything) so generic scenes stay silent rather than recalling the chatter.
- **Temporal production** (a sequence of vocal controls) so it says the sound's trajectory, and **a consonant source** — the road from "makes the bus's vowel-timbre" toward "says a word".
- **Close the last gap to language**: names. A distinct *object* (not scene) discovered visually, bound to a distinct *spoken token* — object → word, the thing this whole architecture was built to eventually reach.

---

## 035 — The bridge: it tries to SAY the sounds it discovered by watching
*2026-07-21 · `SyntheticMind.Voice --say` · the two branches meet*

The two halves have been built separately: an **ear** that discovered recurring sound-units by watching Ms Rachel with no labels (findings 031/032), and a **mouth** that learns to reproduce a target sound by babbling (finding 034). This closes them into one loop — perception's discovered sounds become the mouth's production targets.

Each discovered audio unit's closest real clip (saved by the exemplar dumper) is handed to the babbler as a target. It babbles 600×, then tries to reproduce each with its own vocal tract, matching **spectral shape** (`normalizeMel` — the discovered sound is a different instrument at a different level, so timbre, not loudness, is what "saying it" means).

**Result — 48 discovered sound-units:**
```
  average 35% closer than chance; 27/48 it can approximate well (>30% closer)
  best: unit u34 67% closer · u15 61% · u43 56% · u22 56% · u08 55%
```
It reproduces roughly half the discovered sounds decently, the best ones well. And the whole chain from raw video to this has **no labels anywhere**: watch Ms Rachel → discover recurring sound-units → say them. Heard-vs-said pairs written to `voice-out/say/`.

### Honest limits

- **It can say about half of them.** The ~21 it can't are consonant-, music- or noise-heavy — outside a vowel-only tract. It says the vowel-like discovered sounds; the rest it can only gesture at.
- **Static, not temporal.** The target is the *averaged* timbre of a 0.7 s clip, and the reply is a held vowel aimed at that average — not the sound's trajectory over time. A held "ahh", not the word.
- **"Closer than chance" is a spectral-shape score, not intelligibility.** It aims its formants at the discovered sound; it is not speaking words.
- **Target is the nearest exemplar clip, not the abstract unit prototype** (the codebooks were cleared during earlier tests; the closest real clip is an honest, arguably better stand-in).
- **The video grounding isn't in the loop yet** — it says the sound-units, but doesn't yet go *see a scene → recall its bound sound → say it*. That full see→say loop is the next bridge.

### Next

- **Temporal control**: a *sequence* of vocal-tract settings so it can say a sound's trajectory (a syllable), not one held average.
- **A consonant source** so it can approximate the other half of the units.
- **Close see→say**: recognise a learned visual scene → recall the sound-unit bound to it (the PMI pairings) → say that. Perception all the way to production, grounded end to end.

---

## 034 — The mouth: it learns to make sounds by babbling and hearing itself
*2026-07-21 · `SyntheticMind.Voice` · the action loop, closed*

Everything before this was perception. This is the first time the system *acts on the world and learns from the consequence* — the loop the project kept circling (ARCHITECTURE §5): perception → **action** → perception, from error, no teacher. It's given a vocal tract it knows nothing about, and it learns to use it the way an infant does: babble, hear yourself, then reproduce what you hear.

- **`FormantSynth`** — the vocal tract: an additive source-filter synth, three knobs (F0, F1, F2). Formants F1/F2 *are* the vowel space, so different knobs make audibly different vowel-like sounds. Dumb and fixed, like the cochlea — the motor counterpart to the ear.
- **`VocalBabbler`** — tries random controls, **hears the result through the very same `Cochlea`** it perceives the world with (a `hear` delegate: controls → mel), learns a forward model (controls → sound) online, and remembers every (controls, sound) it makes. To imitate a target sound it recalls the nearest babble and refines it *by listening* (hill-climb on real mel distance).

**Results (400 babbles):**
```
  forward-model error (predicting its own voice): 58.7 -> 33.3   (learning what its mouth does)
  imitating held-out targets in its own range:    95% closer than a random attempt
      target 3: distance 0.234 vs chance 12.221   (98% closer — near-perfect reproduction)
  imitating a REAL human voice (a slice of JFK):  21% closer than chance
```
It reproduces sounds in its own range almost exactly — genuine vocal imitation, learned from nothing but its own babble and its own ear. WAVs written to `voice-out/` (verified 61/61 tests: the synth makes distinguishable vowels, babbling lowers forward error, imitation beats chance by a wide margin).

### Honest limits

- **Vowels only.** The synth makes voiced formant sounds — no consonants, fricatives or plosives. It can approximate a held vowel, not speech.
- **Real-voice imitation is weak (21%)** and that's the honest ceiling: a formant buzz is a different instrument from a human, and real speech carries content this tract simply cannot make. It aims its formants at the target; it can't be it.
- **Static, not temporal.** It imitates an averaged timbre — one held sound, no syllable dynamics, no sequence. A vowel, not a word.
- **The forward model is the smaller half.** The 58→33 drop shows it's learning the map, but imitation leans mostly on the babble memory + listening hill-climb. The learned inverse isn't doing the heavy lifting yet.
- **Not yet wired to perception.** It imitates arbitrary targets, but doesn't yet try to *say the sound-units it discovered watching videos*. That bridge is the point of having both halves.

### Next

- **Bridge mouth to ear-grounded concepts**: feed it the discovered audio-unit prototypes (finding 031/032) as targets — it tries to reproduce the sounds it learned by watching. Perception's units become production goals.
- **A richer tract** (a noise source for consonants) to get beyond vowels.
- **Temporal control** (a *sequence* of knob settings) → syllables instead of a single held sound.

---

## 033 — Frame-skip: a real 25%, and an honest lesson about where the time goes
*2026-07-21 · Batch · `SyntheticMind.Watch --stride N` · measured A/B/C on one clip*

Finding 032's colour run took 3.5 hours, and the obvious waste was decoding 216k frames of one video to catch ~500 sparse events. Frame-skip: fully `Read` + process only every Nth frame, cheaply `Grab` the rest. Expected ~3×. **Measured on the smallest clip (55,724 frames, 31 min audio):**

| stride | wall | frames processed | events | audio surprise |
|---|---|---|---|---|
| 1 | 281 s | 55,724 | 221 | 0.616 → 0.585 |
| 3 | 211 s (**−25%**) | 18,575 | 260 | 0.616 → 0.585 |
| 5 | 199 s (−29%) | 11,145 | 225 | 0.616 → 0.585 |

**Quality holds** — audio surprise identical, event counts comparable, unit counts close (55/51/46). But the speedup is only ~25%, and the numbers say exactly why: processing ⅓ the frames saved ¼ the time, so the per-frame *vision* work (resize, colour deinterleave, 1296-feature retina, predictor) is just ~36% of wall time. The other ~64% is **fixed cost frame-skip can't touch**:
- **The H.264 decode still happens on skipped frames.** `Grab` advances by decoding into the codec's buffer; it only skips the `Retrieve`/colour-convert. Truly skipping decode needs keyframe seeking — unreliable at small strides, so not worth it.
- **Audio is never skipped** — every 10 ms hop up to the last processed frame still runs the cochlea + predictor, by design (audio drives half the events).
- ffmpeg audio extraction per video is fixed too.

So the honest lesson: the bottleneck was never the thing that looked wasteful (per-frame vision) — it's decode + audio, both fixed. Frame-skip banks a free, quality-preserving 25% and is kept (default stride 3; `--stride 1` for a high-fidelity final run), but the big-multiplier speedup isn't here without deeper (riskier) surgery.

### Next

- Leave runtime here — 25% for free, no quality cost, and the remaining bottleneck (decode) isn't worth the reliability risk. **On to the mouth.**
- If runtime ever really bites: per-video parallelism (independent decode) is the bigger lever than frame-skip, but it complicates the sequential shared-state learning.

---

## 032 — A sharper, colour eye: finer in places, still lumping in others
*2026-07-21 · Batch · `SyntheticMind.Watch` · colour retina, re-run on the same corpus*

The finding-031 units were scene-level, and the eye was throwing away colour (`BGR2GRAY`) — the single biggest cue in this content (pink Ms Rachel, red Elmo, green backdrop, teal vocabulary cards are identical in brightness). So we sharpened it: **mean R/G/B per cell**, grid 10→12, input 80×60→120×90, video codebook 64→96. Feature width 600 → 1296. Then re-ran the whole corpus (one decode pass — learn + exemplars together, the finding-031 fix).

**Result: a genuine but partial win.** Spread stayed healthy (busiest video unit 12.8%, down from 15.6%; 31 of 96 units carry ≥1%). And colour surfaced scene-types the grey eye *couldn't* have found:
- **video #88** — the "vocabulary card" segment: teal polka-dot backdrop with a labelled object card ("Towel"). Keyed on that distinctive teal. Internally coherent, genuinely specific.

But other units still **lump distinct scenes**:
- **video #60** (68× with audio #17) — its exemplars mix the *living-room airplane set* (blue couch, checkerboard floor) with the *outdoor counting scene* (green hills, pond). Two clearly different scenes, one unit.

**Why the lumping persists.** The 96-slot codebook fills early (by the 2nd or 3rd video), and once full every later event is force-merged into its nearest existing unit — so a distinct scene that arrives late (the airplane set) gets absorbed into whatever is closest rather than starting its own unit. The bottleneck is no longer colour; it's **capacity + the fill-then-force-merge dynamic**. Colour added real discriminative *information*; the codebook just can't hold enough distinctions to use all of it.

### Honest limits

- **Still scene-level, not object-level.** Colour made scenes *cleaner and more specific* (the teal cards), not "Elmo" or "the number 2". The whole-frame summary can't isolate an object from its scene.
- **Mixed coherence.** Some units sharpened (#88), some still lump (#60). Not audited at scale — this is two hand-picked units, best and worst of the ones inspected.
- **It got expensive.** ~3.5 hours vs. ~40 min for grey — colour + 2.25× the pixels tripled-plus the per-frame cost. **Runtime is now the practical bottleneck**, which makes the next experiments painful.
- Video capacity (96) fills to the cap, so the count is capped-out, not settled — genuine distinctions past 96 are lost.

### Next

- **Frame-skip** (process every Nth frame — events are sparse, we decode 216k frames to catch ~500 events). 3–5× faster, near-zero loss. Makes bigger experiments affordable — do this first.
- **Raise video capacity** (128–192) now colour gives it more to separate, and see if #60-type lumps split.
- **Toward object-level**: attention to sub-regions instead of a whole-frame summary, and top-N units per event so PMI has real within-scene disambiguation.

---

## 031 — We looked: the units are real, coherent, cross-video scenes
*2026-07-21 · Batch · `SyntheticMind.Watch --exemplars` · frames read back by a human (Claude)*

Findings 029/030 proved the *statistics* were healthy (bounded, spread, recurring pairings). But a pairing like "audio #16 ↔ video #45, seen 50×" is still just numbers until you see what unit #45 *is*. So we built the exemplar dumper: a read-only pass that replays the videos with the frozen codebooks and, for each unit, saves the handful of real frames / audio clips closest to its prototype, plus an `index.html` laying the strongest pairings side by side. 28 videos → 63 video + 48 audio units with exemplars (639 files).

**Then we actually looked.** The frames behind the four strongest pairings, read back by eye:

| unit | what it turned out to be | paired sound |
|---|---|---|
| video #45 | Ms Rachel, pink outfit, the flat **cartoon** outdoor backdrop (pond, duck) — same setup across *different videos* | audio #16 (50×) |
| video #26 | a **different presenter** (the man from the Blippi crossovers), grey hoodie, park/city-skyline backdrop, full body | audio #25 (42×) |
| video #41 | brushing teeth at a **bathroom mirror** — the bedtime-routine footage | audio #2 (20×) |
| video #61 | Ms Rachel again, but at a **real** outdoor house — real street, autumn tree, holding crafts | audio #2 (34×) |

Every unit sampled was **internally coherent** (its six frames are the same scene at different moments, often from different videos) and **genuinely distinct** from the others. The sharpest result: **#45 vs #61** — it split "Ms Rachel on the *animated* backdrop" from "Ms Rachel at a *real* outdoor location." Same person; the visual statistics of flat cartoon colour vs. real textured houses differ, and the eye separated them. Nothing was labelled — this is unsupervised clustering of raw co-occurrence landing on scenes a human would name.

So the full chain closes: **raw pixels + audio → discovered units → a human recognises them as real, distinct scenes**, with nothing supervised in between. That's the project's thesis in four pictures.

### Honest limits

- **These are scene/context units, not word- or object-units.** It found "the outdoor cartoon scene," not "the duck" or "the letter A." Real visual structure, but coarser than object-level grounding. The coarse 80×60 eye is exactly why.
- **The video side was verified by eye; the audio side was not** (can't listen from here). A tell that the sound units are fuzzier: **audio #2 pairs with two different scenes** (#41 bathroom, #61 real-outdoor), which reads as a broad "someone talking" unit, not a specific sound. The `index.html` players let a human close this half.
- **Only the strong pairings were sampled** — the best case. Rarer units may be mushier; not audited.
- **Grounding is at the level of whole audiovisual context**, i.e. "this scene and this sound recur together," not "this word means that thing." A real but pre-linguistic kind of meaning.
- Three full decode passes were paid to get here (buggy learn, fixed learn, exemplar dump). The dumper should ride *inside* the learning pass next time — decode once.

### Next

- **Fold exemplar-dumping into the learning pass** so we decode once, not three times.
- **Push toward object/word units**: a sharper eye (finer retina), and top-N units per event so PMI has real within-scene disambiguation to do — the road from "scene grounding" to "word grounding."
- Audit a random sample of units (not just the strong ones) to measure how many are coherent vs. mush.

---

## 030 — The eye was collapsing: fixing it made real sound↔sight structure appear
*2026-07-20 · Batch · `SyntheticMind.Watch` · measured on 28 of 39 Ms Rachel videos, 12,992 co-occurrences*

Finding 029 killed the concept *explosion*. Reading the codebooks it produced revealed the opposite failure hiding underneath: the video side had **collapsed**. Of 12,996 events, **98.4% landed on a single video unit** — the eye wasn't telling scenes apart at all, so the binding had nothing to work with (every sound co-occurred with the one video unit, PMI ≈ 0).

**Cause.** The vector fed to the video quantizer was a heavy running average (EMA) over 600 whole-frame edge features. A talking-head kids' show is nearly the same frame every time; averaged over time it becomes *almost identical every event* (cosine ≈ 1 everywhere), so everything merged onto one unit. Audio dodged this — speech and song are sharp and transient even when smoothed.

**Fix** (made a testable property of `VectorQuantizer`, opt-in so audio is untouched):
- **`subtractRunningMean`** — match on each input's *deviation* from a running input mean, so the always-present "typical frame" cancels and what's left is what actually distinguishes one scene from another.
- **Quantize the instantaneous event frame**, not the time-blurred summary.

A test reproduces the exact failure (baseline-dominated vectors → ≤2 units) and proves centering recovers the distinct scenes (≥ classes units). Suite 57/57.

### Result on the real corpus (28 videos opened; 11 wouldn't decode)

| | before fix | after fix |
|---|---|---|
| busiest video unit | **98.4%** of events | **15.6%** of events |
| video units carrying ≥1% | 1 of 13 | **29 of 64** |
| audio units | 48/48 (already healthy) | 48/48, busiest 21.7% |
| distinct pairings recurring ≥10× | ~0 meaningful | **319** |

The eye now discriminates, and real cross-situational structure appears — pairings with **both** high PMI and high support (they recur many times, so they're not flukes):

```
  a#25 <-> v#26   pmi 1.77, seen together 42x
  a#16 <-> v#45   pmi 1.48, seen together 50x
  a#2  <-> v#61   pmi 1.20, seen together 34x
  a#2  <-> v#41   pmi 1.56, seen together 20x
```

These are specific sound-units repeatedly co-occurring with specific scene-units *across many different videos* — exactly what cross-situational binding is meant to surface, and impossible when one unit swallowed everything.

### Honest limits

- **Structure recovered ≠ concepts named.** `a#25 ↔ v#26` is a real, robust statistical pairing — but we don't yet know *what* it is (a particular song and its on-screen card? a phoneme and a mouth shape?). Interpreting them means dumping the exemplar frames/sounds behind each unit — not done here.
- **Both codebooks hit their cap** (48/64). Capacity is now the binding constraint, so the granularity is whatever fits in 48+64 slots — chosen, not derived. Real distinctions finer than that still blur together.
- **PMI and support trade off.** The single highest PMI (a#30↔v#41, 3.75) was seen only 16×; the *most trustworthy* pairings are the high-support ones above. The default min-support (3) is lenient — a 3× pairing on 12,992 episodes is thin.
- **11 of 39 videos never decoded** (~28% of the corpus lost to codec/download issues) — a data-plumbing loss, not a learning one, but the run saw less than it should have.
- Still one dominant unit per sense per event, so PMI is doing frequency-discounting, not the harder within-episode disambiguation it's built for.

So: the explosion and the collapse are both fixed, and on a curated single-subject corpus the system now surfaces bounded, recurring, cross-video sound↔sight structure. The next real work is *interpretation* — opening up what those units actually are.

### Next

- **Dump exemplars per unit** (a few frames / audio clips nearest each prototype) so a human can see what `a#25` and `v#26` actually are — the difference between "statistics" and "concepts."
- Let capacity breathe (or make it adaptive) and see whether finer, still-stable distinctions emerge.
- Re-fetch the 11 unreadable videos; consider top-N units per event to give PMI genuine disambiguation to do.

---

## 029 — Bounded consolidation: from a 10k-concept pile to a handful of recurring units
*2026-07-19 · Batch · `SyntheticMind.Watch` · explosion measured on a real 39-video corpus; fix built + tested*

Finding 028 said "feed it a curated corpus and see." We did — 39 Ms Rachel toddler-learning videos (one presenter, one subject: the consistency finding 022 asks for). Two very different things happened, and both are the point.

**The learning worked.** Audio surprise fell within and across clips and *stayed* fallen as new videos arrived — it was adapting to her voice and songs:
```
  001  65029 frames, 2169s | audio surprise 1.503 -> 0.903
  002 143096 frames, 4774s | audio surprise 2.187 -> 0.736
  003 108503 frames, 3620s | audio surprise 0.897 -> 0.723
  004 110536 frames, 3688s | audio surprise 0.637 -> 0.657
  005 108897 frames, 3633s | audio surprise 1.293 -> 0.679   (settling ~0.68)
```

**The concept count exploded.** +174, +492, +243, +168, +181 — **>1,200 "concepts" after 5 clips**, heading for ten thousand by video 39. Not a thousand things learned: the over-splitting failure live. Because every real-video moment is a little different, the novelty-gated store minted a *new* concept almost every event instead of consolidating. A giant undifferentiated pile, not a vocabulary.

### The fix — two parts, both small

- **Bounded codebook (`VectorQuantizer`).** The real consolidation. A codebook that can hold at most `capacity` prototypes (48 audio, 64 video). A novel event mints a new unit only while there's room; once full, it snaps to (and nudges) the nearest existing one. So the unit count has a **hard ceiling** — it *cannot* explode, no matter how many videos. Recurring things land on the same unit id again and again.
- **Cross-situational PMI layer (`CrossSituationalBinder`, from finding 022, now wired in).** Over those unit ids it accumulates co-occurrence across the whole corpus and ranks pairings by pointwise mutual information — which divides out how common each unit is. Ms Rachel's face is on screen almost constantly, so naive counting pairs it with *every* sound; PMI discounts the ever-present and lets the specific, recurring sound↔sight pairings rise. A support floor (min joint count) keeps one-off coincidences off the chart.

### Verified

- Unit tests: the codebook provably never exceeds capacity (5,000 all-novel vectors → exactly 16 units at cap 16); recurring vectors keep the same id (>95%); survives save/load. PMI ranking surfaces the true recurring self-pairs and rejects both a constant distractor and a one-off coincidence. Full suite 56/56.
- End-to-end on `bbb.mp4` (10 s): 4 events → **2 audio + 1 video unit** — bounded, no pile.

### Honest limits

- The **explosion is fixed; meaning is not proven.** A bounded, PMI-ranked set is the right *shape*, but whether the units correspond to things a human would name still depends on the coarse eye/ear (findings 019/024) — 64 video units over an 80×60 grayscale retina is still gross.
- **`capacity` and the split thresholds are chosen, not derived.** Too small blurs distinct things together; too large lets noise back in. Sensible defaults, untuned on this corpus.
- One dominant unit per sense per event (no within-episode ambiguity), so PMI here is doing the *frequency-discounting* job, not the harder *disambiguation* job it's built for. Still the right tool; just working easier data than the 022 tests.

### Next

- Re-run the 39-video corpus with the bounded pipeline and read the top PMI pairings — the actual test of whether curation + consolidation yields real concepts.
- Tune `capacity`/thresholds on that output; consider top-N units per event to give PMI genuine disambiguation to do.

---

## 028 — It watches video files, unattended: batch cross-modal learning
*2026-07-17 · Batch · `SyntheticMind.Watch` · verified on a real MP4*

The automation ask: point it at a folder of videos and let it watch *and* listen on its own, no human pressing SPACE. Built `SyntheticMind.Watch` — OpenCvSharp reads the video frames in-process, ffmpeg (already on PATH, shelled out) pulls the audio track to a temp WAV. The two run in sync; when both senses spike together it auto-binds the co-occurring pair into a **persistent** `CrossModalStore`, so learning accumulates across the whole pile and across sessions.

**Verified on a real MP4** (Big Buck Bunny, 10 s, h264 + aac):
```
  bbb.mp4   250 frames, 10s audio | bound 4 events, +3 concepts (audio surprise 0.147 -> 0.123)
  (second run) starting from 3 concepts -> bound 4 events, +0 new
```
The whole chain works: audio extracted, frames decoded, both pipelines synced, events auto-bound with no trigger, learning persisted. The second run *reinforced* the same concepts rather than duplicating them (+0 new) — re-watching consolidates, exactly right.

### What this is

The full unsupervised loop at file scale: perceive two senses from a video → learn on the fly → detect salient co-occurring events (adaptive surprise threshold + cooldown) → bind them cross-modally → remember. Feed it a folder, walk away, come back to what it formed. Every piece is one we already built and tested; this wires them into an unattended batch runner.

### The honest limits — and they are the whole story here

- **"3 concepts" from one random 10-second clip means almost nothing.** They're whatever happened to spike together in Big Buck Bunny. This finding verifies the *machinery*, not that it learned anything meaningful.
- **Meaning needs consistency, and random video hasn't got it.** This is finding 022's conclusion made concrete: cross-situational binding recovers real pairings only when they *recur* across many episodes. A curated pile (many clips of the same thing) could firm up; a diverse grab-bag washes into statistical soup. What you feed it matters far more than how much.
- **The event thresholds are guesses** (spike = 2× running baseline, 400 ms cooldown), untuned on real footage — the auto-binding will be noisy and want tuning per corpus.
- Coarse eye and 20-band ear still can't tell fine things apart, so even with perfect co-occurrence the concepts are gross.

So: a working, unattended, watch-and-listen learner that accumulates — and an honest reminder that scale without consistency is the frontier, not the solution.

### Next

- **Feed it a curated corpus** (many clips of one kind of thing) and see whether a real, recurring concept firms up — the actual test of unsupervised learning at scale.
- **A cross-situational layer over the auto-binds** (finding 022's PMI) to sort real pairings from coincidences, instead of trusting every co-event.
- Tune the event detector on real footage.

---

## 027 — The top-down loop: a pipeline becomes a mind
*2026-07-17 · Predictive hierarchy · `SyntheticMind.Mind` (Rules, Hierarchy), `TopDownTests`*

Every finding until now flowed one way: senses up to concepts. [ARCHITECTURE.md §5](ARCHITECTURE.md) warned that this makes it *a pipeline, not a mind* — a real cognition needs the reverse edge too, higher levels shaping what lower levels do. The `Unit` always had a dormant `context` port for exactly this (plumbed by `Hierarchy`, ignored by every rule). This activates it: `LinearDeltaRule` now learns from the context handed down, so a higher level can steer the lower level's prediction.

**The task** is a long-range dependency the fast level *structurally cannot* solve: each block is `[cue, 0,0,0,0,0,0,0, cue]` — the final value echoes a cue from 9 steps back, past level 0's 4-frame window.

```
  error at the target (needs the cue from 9 steps back), lower = better:
    no context (fast level blind to the cue)   0.917   baseline
    + oracle cue (top-down truth)              0.750   18% lower — can the fact help?
    + real level-1 state (the loop)            0.618   33% lower — does the real loop help?
```

### The result

**Top-down feedback cut the error 33% on something the fast level cannot predict alone.** Level 1 held the cue in its longer memory; the `Hierarchy` fed its state down as level 0's context; level 0 learned to use it and predicted across the gap. Information flowed *down*, and it mattered.

Notably the real loop beat the bare-cue oracle (33% vs 18%): level 1's state carries *when* as well as *what* — where in the block we are, not just the cue value — so level 0 knows both the cue and where to apply it. A learned top-down signal can be richer than the single fact you'd think to hand down.

### Why this one matters more than its size

This is the edge the whole design was missing. Until now the system perceived, learned, and grounded — all bottom-up. A mind also *predicts top-down*: what you expect shapes what you perceive; a concept primes its parts; memory reaches down into the senses. This is the smallest honest instance of that — a higher level's memory improving a lower level's prediction — but it's the difference in kind between a feed-forward pipeline and something that loops. The port existed since the first design; now it carries signal.

### Honest limits

- **One direction of benefit, one simple task.** It shows top-down *prediction* helps on a long-range dependency. It is not yet attention (context *gating* perception), nor pattern completion in the live senses — those are the same edge pushed further.
- **Only `LinearDeltaRule` uses context so far.** The learned units still ignore it; wiring it into them (and into the live audio/video hierarchy) is the next step.
- The effect is real but modest (33%) on a toy; whether top-down scales to sharpen real perception is untested.

### Next

- **Context in the learned units**, and in the live perceiver — does knowing "what I'm hearing" sharpen "what I'm seeing", and vice versa?
- **Attention:** let context *gate* which inputs a level attends to, not just bias its prediction.

---

## 026 — Persistence: what it learns survives a restart
*2026-07-17 · Memory · `ConceptStore`, `CrossModalStore`, `SyntheticMind.Perceive`*

Until now every concept was forgotten on exit — teach it "ahhh + wave-left", quit, and it's gone. Both grounding stores now save and load (JSON), and the live perceiver uses it: it loads its memory on start and saves on quit.

**Verified:** round-trip tests for both stores — teach, save to disk, load into a fresh store, and recall still works (the sound still recalls the right concept, the shape still recognizes). `LoadOrNew` starts empty when there's no file. The live perceiver prints "remembered N concepts from last time" on launch and "saved N concepts" on quit.

### What this changes

The mind now has a **lifetime**, not just a session. Teach it something today, and tomorrow it still knows it — the first step from "a demo you re-run" toward "a thing that accumulates." Combined with the one-shot, no-forgetting binding, that means learning genuinely persists: each session adds to what came before instead of starting from zero.

### Honest limits

- **It persists the bindings, not the predictive hierarchy.** The `ConceptStore`/`CrossModalStore` prototypes are saved; the level-0 encoders (which re-adapt quickly anyway) are not. So the *grounded concepts* survive, but the perceptual front-end relearns its fast statistics each run. For grounding that's the part that matters; for a fuller "resume exactly where I was", the encoders would need saving too.
- JSON is bloated for float arrays but readable — you can open the file and see what it knows. Right choice for a project about understanding, at this scale.

### Where the project stands

The core loop is now complete end to end and durable: perceive (two senses, live) → learn on the fly → discover units → ground to concepts, cross-modally, by example → **remember across restarts**. What began as "an AI that can do anything" is a small, honest, working mind that learns from its senses and keeps what it learns. The frontier remains scale and the mess of the real world — but the machine underneath is whole.

### Next (open)

- **Save the encoders too**, for exact-resume.
- **Push the sharper eye live** — teach real objects and find where it holds and where it blurs.
- **Scale** — many concepts, longer sessions, and the accumulation that persistence now makes possible.

---

## 025 — A sharper eye: oriented edges, the V1 the retina was missing
*2026-07-17 · Vision · `SyntheticMind.Vision.Retina`*

The coarse 8×8 brightness+motion retina could feel *that* something moved, not *what* it was — finding 024's live grounding only worked on gross gestures. This gives the eye what a real V1 has: **oriented edge detectors**. Per grid cell, a small histogram of gradient orientations (HOG-style) — where the edges are and which way they run — on top of brightness and motion. Edges carry *shape*, which is what tells objects apart when brightness can't.

**Verified (feature level):** a vertical edge lights the horizontal-gradient bin, a horizontal edge lights the middle bin, and the feature vector widens as configured. The live perceiver now runs a 10×10 grid with 4 orientation bins — 600 features (brightness + motion + 4 edges per cell), up from 128.

### Honest status

- **The eye is genuinely richer, but "sharper" is relative.** 600 hand-built features over a 10×10 grid is a real step up from a blurry 8×8, and edges add shape it never had. It is still nowhere near what a deep vision model extracts — it will help tell distinct *shapes* apart (a mug outline vs. a book), not recognize a specific face or read text.
- **Whether it improves live grounding is pending a hardware test.** The feature is correct and wired in; the payoff — binding more, and subtler, real things — is yours to try. The old grounding tests still pass unchanged (they used motion-only on distinct synthetic shapes, which never needed edges).
- The bigger feature vector is cheap: NLMS (finding 013) already makes the encoder scale/dimension-agnostic, so no re-tuning was needed — the same code just eats a wider input.

### Next

Persistence — save and reload the learned bindings and prototypes, so a session's grounding survives a restart (right now every concept is forgotten on exit).

---

## 024 — Live cross-modal grounding: teach by co-occurrence, recall across senses
*2026-07-17 · Live · `SyntheticMind.Perceive` · verified on hardware by the user*

`SyntheticMind.Perceive` gained interactive binding: make a distinct sound while doing a distinct gesture, press SPACE, and it binds the co-occurring (heard, seen) pair. Then either sense recalls the pair — A takes the current sound and finds the matching pair, V takes the current gesture.

**Result (user-verified, live):** taught two pairs — "ahhh" + wave-left → pair #0, "ooo" + wave-right → pair #1 — then tested recall repeatedly. It was **consistent**: left gesture always recalled #0, right always #1, and the sounds likewise. The eye meter visibly responded to movement, so the visual side had real signal to bind, not a blank. Cross-modal grounding, on real hardware, in real time.

This is the whole arc, live: perception through two senses (023), and now binding one to the other from co-occurrence alone — the finding 021/022 mechanism, running on a mic and a webcam with a person teaching it by example. Not a unit test (needs hardware); a qualitative result confirmed by use.

### A real bug fixed on the way

SPACE wasn't registering at first (`bound:` stuck at 0). It was the console input: polling `KeyAvailable` inside the fast redraw loop didn't deliver keys in this terminal. A dedicated blocking `Console.ReadKey` thread fixed it, plus per-key feedback so input is always visible. (And a reminder logged: this repo is developed on the same machine that runs it — no fetch/pull needed between edit and run.)

### The honest ceiling — unchanged, and now felt directly

- **Two pairs, grossly distinct.** Reliable because "wave left" vs "wave right" and "ahhh" vs "ooo" are unmistakable to a coarse 8×8 retina and a 20-band ear. More pairs, or subtler distinctions, will degrade — the retina genuinely can't tell fine things apart.
- **It bound representations, not concepts** (as in 020/021). It knows *this gesture* goes with *that sound*. It has no notion of what either *is*.
- The gesture had to be *happening* at the moment of binding (the perception summary fades in ~0.3 s) — grounding is tied to real-time co-occurrence, which is honest but finicky.

### Where the project stands

From "an AI that can do anything" (message one) to: a modality-agnostic predictive learner that learns real sound and real video on the fly, discovers the units of speech unsupervised, grounds perception to names, and — live, on a webcam and mic — binds what it hears to what it sees by example. Every claim measured or demonstrated, every limit named. The remaining frontier is exactly the mess it was spared: a sharper eye, many concepts, real ambiguity, and the scale of grounded experience a mind actually needs.

### Next (open, no wrong answer)

- **A sharper eye** — edge/orientation features and finer resolution, so it can bind real objects, not just gross gestures.
- **Persistence** — save/load the learned bindings and prototypes so a session's learning survives a restart.
- **More pairs, live** — push the live binding until it breaks, and see where.

---

## 023 — Live: it listens and watches the real world at the same time
*2026-07-17 · Live · `SyntheticMind.Perceive` · verified on hardware by the user*

`SyntheticMind.Perceive` runs both senses at once on real hardware — microphone → cochlea → level 0, and webcam → retina → level 0, each on its own thread at its own rate, both showing live surprise.

**Result (user-verified):** the camera opened, the mic worked, and both meters jumped in response — the EAR to talking, the EYE to movement. The same architecture that began as a bouncing dot now perceives real sound and real sight, simultaneously, from the room.

This one can't be a unit test — it needs a mic and a camera — so it's a qualitative result confirmed by running it, not a measured number. That's the honest status: the live dual-sense loop works. Camera device 0 was correct, the default bar scales were legible, no fixes were needed on the first run.

### What this is

The perception half of the "companion that watches and listens to the room" — real, live, running. Everything upstream (learning, segmentation, grounding, cross-modal binding) was demonstrated on files and synthetic streams; this confirms the front ends and the simultaneous loop hold up on actual hardware.

### What it is not

Perception, not yet cognition-in-the-room. It *reacts* (surprise) to live sound and sight; it does not yet *bind* what it hears to what it sees in real time, and the coarse 8×8 retina can't tell real objects apart. Those are the next steps, now on a foundation that provably runs live.

### Next

- **Live cross-modal binding:** bind co-occurring live audio and video (the finding 021/022 mechanism, in real time) — make a sound while showing something, and let it associate them. Limited by the coarse retina, but the first live grounding.
- **A sharper retina** so the eye can actually distinguish real things, not just motion and gross brightness.

---

## 022 — Cross-modal binding survives the mess, and it fails in exactly one place
*2026-07-16 · Cross-modal · `SyntheticMind.Mind.CrossSituationalBinder`, `CrossSituationalTests`*

Finding 021 bound heard-to-seen on *clean* co-occurrence (one object per episode). The real world is ambiguous: many things at once, no single moment telling you which sound goes with which sight. This is the honest test of whether binding survives that — via cross-situational learning (Yu & Smith), the leading account of how infants learn words: the true pairing is the one that stays consistent across episodes, recovered by pointwise mutual information.

```
  8 objects, 3 present per episode.  binding accuracy (chance 12%):
    ambiguity + missing modality + spurious pairs      100%
    weak pairing — correct sight present only 30%       100%  (500 episodes)
    weak pairing 15%                                     84%
    exposure needed (clean pairing)                     100% by 20 episodes
    the hard corner: 30% pairing, 100 episodes           92%
```

### The result — it's more robust than expected

**Cross-situational binding shrugs off the mess.** Multiple objects at once, missing modalities, spurious distractors, and a pairing that only holds 30% of the time — it still recovers the true mapping, and it's data-efficient (tens of episodes, not thousands). PMI is why: normalizing by base rates means a weak-but-*consistent* correlation beats a frequent-but-incidental one. This is exactly why infants learn words from genuinely ambiguous input — the mechanism is real and it works. It only frays at extreme weakness (15% co-occurrence).

### The one place it genuinely fails — and it's a real limit, not a bug

**Perfectly correlated distractors.** If two objects *always* appear together, no statistic on earth can tell which sound goes with which sight — their co-occurrence with each other's referent is identical. The binder correctly collapses a glued pair into an unresolvable set while still recovering every independent object. This isn't a flaw to fix; it's a true property of the problem (you cannot disambiguate two things you've never seen apart), and it's pinned as a test so it stays honest.

### What this settles, and what it doesn't

- **Settled:** the *binding* mechanism is not the bottleneck for grounding. Given identified units, recovering their cross-modal pairings under real ambiguity is robust and cheap. The rung-4 wall is not here.
- **Not settled — and this is where the wall actually is:** this test assumes the *units are already identified* (clustering solved — findings 016/017). The hard part of real grounding is the **perception under mess** — segmenting and clustering units correctly when the world is continuous, noisy, and overlapping — and **scale** (thousands of concepts, correlated co-occurrence, limited grounded experience). The binding statistics were never the hard part; identifying *what* to bind, at scale, is.

So the honest map of grounding sharpens: binding — solved and robust. Perception-under-mess and scale — the real remaining frontier.

### Next

- **Join the two ends:** cluster real (noisy) audio+video summaries into units *and* cross-situationally bind them, in one unsupervised pipeline — does binding survive real clustering error, not just assumed-clean units?
- **The live version:** mic + webcam together — grounding what's actually in the room.

---

## 021 — Cross-modal grounding: meaning without a teacher (rung 4)
*2026-07-16 · Cross-modal · `SyntheticMind.Mind.CrossModalStore`, `CrossModalTests`*

The wall we kept naming: bind a *heard* thing to a *seen* thing with **no labels** — only co-occurrence in time. Five objects, each a distinct sound (tone pair) paired with a distinct sight (moving shape). They're presented together and a `CrossModalStore` binds each co-occurring pair. Nobody says what anything is; being simultaneous is the whole signal.

```
  bound 60 co-occurring sound+sight episodes (no labels)
  HEAR the sound → recall the right SIGHT:  100%   (chance 20%)
  SEE  the sight → recall the right SOUND:  100%   (chance 20%)
```

### The result

**It listened and watched at the same time, and grounded one sense in the other, unsupervised.** After binding, hearing a sound recalls the correct sight and seeing a sight recalls the correct sound — both perfectly on this set. The system learned that *this noise goes with that look*, from nothing but their happening together. That is the closest the project has come to **meaning without a teacher** — the rung-4 target named as far back as the "how would it learn English" conversation.

Mechanically it's the fast store / Concept System one more time, now spanning two senses: novelty-gated prototypes, each holding a sound signature and a sight signature, recalled cross-modally by nearest prototype in one modality. The whole architecture from the first message is now present and connected: modality-agnostic predictive hierarchy (014, 019), perceptual grounding (020), and cross-sensory binding (021).

### The honest limits — and they are the whole distance to real meaning

- **Five distinct objects is the easy case.** 100% because each object is unmistakable in *both* senses. Similar objects, or many of them, would fall off hard — the coarse retina and 20-band cochlea can't resolve fine distinctions.
- **The co-occurrence was clean.** Exactly one object per episode, sound and sight perfectly aligned. Real co-occurrence is a mess: many things present at once, sounds without sights, referential ambiguity (the child hears "dog" — is it the animal, the fur, the barking?). That ambiguity is most of what makes real grounding hard, and none of it is here.
- **It bound *representations*, not *concepts*.** It knows sound-A goes with sight-A. It does not know either is a "dog", cannot generalize to a novel view, and has no notion of the thing beyond the two signatures. This is associative memory across senses — a real and necessary piece of meaning, but not the whole of it.

### Where this leaves the project

Every rung has now been touched: sound/vision learning (form), segmentation and unit discovery (structure), naming (label grounding), and cross-modal binding (meaning without a teacher, in the clean case). The mechanism is real and the pieces connect. What separates this from *understanding* is exactly the mess it was spared — ambiguity, scale, generalization, and the sheer amount of grounded experience a mind needs. Those are not the next feature; they're the real frontier, and naming them honestly is the point.

### Next

- **Messier co-occurrence:** sounds without sights, two things at once, weak/partial pairing. Does binding survive noise, or does it need a cleaner signal than the world gives?
- **The live version:** mic + webcam together (needs a camera), so it grounds what's actually in the room — the original companion vision.

---

## 020 — Grounding: it learns names for what it sees. Slice 0 comes home.
*2026-07-16 · Vision + Concept · `SyntheticMind.Mind.ConceptStore`, `GroundingTests`*

The first grounding: bind a visual thing to a name. A script teaches five distinct moving "things" — rightward, downward, diagonal, orbit, blink — each shown with its label, then quizzes recognition on fresh, varied instances (new speed, phase, position, noise).

```
  taught 5 things, 8 examples each (script supplied the labels)
    rightward  10/10     downward 10/10     diagonal 10/10     orbit 10/10     blink 10/10
  overall grounding accuracy: 100%   (chance 20%)
```

### The result

**It learns names for what it sees and recognizes new examples — 100% here.** This is Slice 0's founding idea ("teach it a thing, ask what it is"), abandoned early to go build the learning machinery, now closed on the real pipeline: a retina instead of a rented CLIP, a `ConceptStore` (the fast store / Concept System) doing the binding. A visual pattern is no longer just a recurring blob — it has a referent, a name.

### What this is, architecturally

Grounding is a **different operation** from the predictive hierarchy, and the project now has both — as the original architecture said it should. The hierarchy *learns to predict* a stream; the `ConceptStore` *binds* a representation to a symbol. And it grounds the **perception** (the retina summary), not the learned encoder — because categorical structure lives in perception (findings 008, 015, 016; a fourth confirmation). The binding is one-shot and cannot forget by construction (each name is its own running-mean prototype).

### Honest limits — and they matter

- **The five things are easy** (motion-distinct: horizontal vs vertical vs orbital vs whole-field). 100% is because they're well-separated, like speech-vs-music was. Visually *similar* things would be far lower — the coarse 8×8 retina can't tell fine detail apart.
- **This is *label* grounding, not the hard kind.** A script *told* it the names. That's real grounding — the pattern now has a referent — but it is not the dream: a system that hears "dog", sees a dog, and binds them **itself**, with no script. That cross-modal, unsupervised binding is the genuine rung-4 wall, and it is still unbuilt.
- The `ConceptStore` is nearest-prototype matching. It's honest and it works, but it's associative memory, not understanding.

### Where the whole project stands

Both halves of the founding architecture now exist and work: a **modality-agnostic predictive hierarchy** (learns any stream — audio 014, video 019) and a **Concept System** that grounds perception to names (020). Sound and vision run through the same learning code; a percept can be bound to a symbol. That is a real, working sketch of the thing the first message asked for — with every limit measured, not hidden.

### Next — the real grounding

Cross-modal, unsupervised: play a clip where a spoken word co-occurs with a seen thing (both discovered, neither labeled), and let the system bind the sound-unit to the visual-unit *on its own*. No script. That is meaning without a teacher — the hard rung, and now the concrete next target.

---

## 019 — A second sense: it watches video, and the architecture is modality-agnostic
*2026-07-16 · Vision · `SyntheticMind.Vision`, `VideoLearningTests`*

Added a vision front-end, exactly parallel to audio: a `Retina` (the eye's cochlea — fixed, dumb, downsampled brightness + motion grids, SCAFFOLD.md §4) and a `VideoStream` that decodes a real video file (animated GIF via ImageSharp, no native codec) into a stream of feature vectors. Then fed a real clip — Wikimedia's rotating-Earth GIF — into the **same** hierarchy the audio used.

```
  earth.gif — 44 frames, 400×400, 128 retina features (8×8 brightness + 8×8 motion)
  mean surprise per loop:  0.0076 → 0.0054 → 0.0045 → … → 0.0030   (2.5× over 8 loops)
```

### The result

**It watches video and learns the motion — same behavior, same code, different sense.** Surprise fell 2.5× as it saw the rotation repeat: it learned to predict the Earth turning, unsupervised, exactly as it learned to predict speech (finding 014). The *only* thing that changed from the audio pipeline is the front end (retina vs. cochlea). The hierarchy, the learning rule, the surprise signal — all identical and untouched.

That is the strongest evidence yet for the founding bet (SCAFFOLD.md §2): **the learning is modality-agnostic.** One mechanism, any stream. Sur's rewired ferrets grew visual cortex out of auditory cortex; here the same unit learns vision or sound depending only on what's plugged in.

### Honest limits

- **The retina is coarse** — 8×8 brightness + motion. Enough to learn "the bright disc is rotating"; nowhere near enough to tell two similar objects apart. Fine for proving the pipeline; a real perceptual front end is much richer.
- **GIF, not MP4.** A dependency-light stand-in for "a video file." Real video (H.264 etc.) needs a codec (FFmpeg); not added.
- **This is still perception, not grounding.** It learned the *dynamics* of what it saw. It has no idea it's an "Earth" — that's the next step, and the reason this sense was built.

### Next — grounding (the point of a second sense)

Now that sound and vision run through the same architecture, a unit in one can bind to a unit in the other. That is rung 4 — meaning — and it's the whole reason to have two senses. The concrete first experiment: a clip where a visual thing co-occurs with a label or a spoken word, and the system binds the seen pattern to it. That's the next build.

---

## 018 — Tier 3: the segmenter fires at syllable rate on real speech, and respects the pauses
*2026-07-16 · Audio · `SyntheticMind.Listen --segment`*

First contact with real speech. Ran the surprise segmenter (no tuning, no labels) on `jfk.wav` — "ask not what your country can do for you" — and looked at where the boundaries land.

```
  43 boundaries in 11.0s  =  3.9 per second     (median gap 160 ms)

   2.5s  (silence)                     ← the pause after "Americans": zero boundaries
   3.0s  #####               |||||     ← dense speech: boundaries cluster
   4.5s  (near silent)                 ← clean
   7.5s  (silence)                     ← clean
```

### What's genuinely encouraging

- **It fires at a linguistically plausible rate — ~3.9/s, median gap 160 ms — which is English syllable/word scale.** Nobody set that; it fell out of the acoustics. English syllable rate is ~4/s. It's finding units at the right grain.
- **It respects silence.** The pause after "Americans" (~2.5 s) and the other gaps have *zero* boundaries. It fires on speech and stays quiet in the gaps — it is not firing on a timer or on noise.
- **It concentrates on dense speech**, tracking the sentence's rhythm.

So the mechanism that worked on synthetic syllables (016/017) produces *speech-like* segmentation on *real* speech: right rate, right silences.

### The honest limits — and they're real

- **No ground truth.** Without phoneme-/word-aligned labels for this clip, I cannot score whether boundaries land on the *actual* linguistic boundaries. The plausible rate and the clean silences are strong circumstantial evidence, not proof. This is the ceiling of what's checkable here; a properly labelled corpus (e.g. TIMIT) is what a real evaluation needs.
- **It's coarser than phonemes.** 3.9/s is syllable/word scale; real speech has ~10–15 phonemes/s. It catches the strong onsets (stressed syllables, word starts), not every phoneme transition.
- **Still form, not meaning** (unchanged from 016). It found *where the units are*, not *what they are* or *what they refer to*.

### Where the language ladder stands now

Rungs 1–3 (sound → segmentation → recurring units) are demonstrated: on synthetic data with hard numbers (016/017) and on real speech qualitatively (018). The mechanism is the infant one — predict, be surprised at boundaries, cluster the pieces — and it behaves sensibly on real input. **Rung 4 (meaning) is untouched and needs grounding — a second sense — which is the real wall.** No amount of more audio crosses it; that takes vision or another channel to bind sound to referent.

### Next

Two honestly-different directions, no wrong answer:
- **Grounding (the hard, important one):** add a second sense so a sound-unit can bind to something outside audio. This is the only path to *meaning*, and it's the original camera-plus-mic vision. Big.
- **Sharper segmentation:** a real labelled corpus to actually score boundaries, and a phoneme-scale grain. Consolidates rung 3 before climbing.

---

## 017 — Tier 2: it scales and discovers pure units, but over-splits the count
*2026-07-16 · Audio · `SyntheticMind.Audio`, `SyllableCountTests`*

Tier 2 raised the bar three ways: **20** unit types (not 5), **per-instance variation** (±6% formant jitter, so no two instances are identical), and — the real new challenge — **the count is not given**. Instead of k-means with k=5, novelty-gated online prototypes (the Slice-0 fast store: near an existing prototype → merge; novel enough → spawn a new one) decide how many units exist.

```
  segmentation recall            0.80   (still finds most boundaries, now over 20 varied units)
  prototype purity               ~0.75  (each prototype maps mostly to one true type)
  discovered count vs true (20):
     vigilance 0.15 → 140    0.25 → 90    0.35 → 59    0.45 → 43    0.55 → 28
  after consolidation (merge): 34–45 units at purity ~0.72–0.75
```

### What held

**The mechanism scales.** Segmentation still works at 20 units (0.80), and the prototypes it forms are **pure** — each is mostly a single true type (~0.75). More units and real within-unit variation did not break it. The units it finds are real.

### What didn't

**It over-splits, and the exact count is genuinely ambiguous.** Every setting finds *more* than 20 prototypes (28–140 depending on the vigilance knob). Each true syllable fragments into ~2 pure sub-prototypes that won't merge — because the ±6% jitter makes within-type spread comparable to some between-type gaps at 20-band mel resolution, so merging them would also merge *different* types (purity falls). Consolidation (a merge pass — the "sleep" idea from the original plan) helps (123 → 34) but can't cleanly recover 20 without trading purity away.

**This is not a bug — it's a real property of unsupervised discovery.** "How many units are there" is ill-posed once within-class variation approaches between-class variation. There is no single correct count without fixing a granularity; the vigilance parameter *is* that granularity, and it trades count against purity along a smooth curve. Reporting the curve is more honest than tuning the stimulus until a pretty "20" falls out.

### The knob has a name

The vigilance/merge threshold is exactly ART's vigilance parameter (Grossberg) and consolidation is exactly the "sleep" step from PLAN.md — both arrived at from need, not theory. The count-vs-granularity tension is the same one every clustering method faces; the honest move is to expose it, not hide it.

### Next

- **Tier 3: real speech.** Point the segmenter at `jfk.wav` and see where the boundaries land against the actual words. Real speech has coarticulation and no clean stationary segments — this is where the mechanism meets reality, and the thing worth being curious about.

---

## 016 — Unsupervised sound-unit discovery, Tier 1: the pieces show up
*2026-07-16 · Audio · `SyntheticMind.Audio`, `SyllableDiscoveryTests`*

The first rung toward language: can the system find the recurring sound-units of a stream with nobody labeling them? Tier 1 uses 5 distinct synthesized "syllables" (formant-like tone pairs) in random order and **random durations** (so it can't cheat by learning a fixed rhythm). Two mechanisms, both reused from earlier findings, both measured against ground truth.

```
  segmentation (surprise → boundaries):  precision 0.78   recall 0.89
  discovery (chunks → k-means → purity): 0.82   (chance ~0.20–0.30)
```

### The result

**Both halves work, with no labels.**
- **Segmentation:** level 0's surprise (prediction error, finding 014) peaks at the syllable boundaries — it caught 89% of them. Sounds *within* a syllable are predictable (low surprise); the change *at* a boundary is not (a spike). This is the mechanism infants use to segment speech, running on our pipeline.
- **Discovery:** the chunks between boundaries, summarized and clustered, sort into the true 5 types at 82% purity — far above the ~0.25 chance floor. The system found *that there are ~5 recurring units* and *which is which*, from an unbroken stream. That's the prototype-memory idea from Slice 0's origin, now fed by discovered speech units instead of labeled objects.

### The recurring lesson, a third time

Discovery clustered on the **perception** (mel), not level 0's learned state — because the max-variance encoder discards this structure (findings 008, 015). The perception preserves the unit identity; the encoder throws it away. Three independent tasks now point to the same architectural fact: *slow/categorical structure lives in perception, and the learned encoder is the wrong place to look for it.* That is starting to look like a law of this architecture, not an accident.

### Honest limits — this is Tier 1 for a reason

- **Synthesized syllables are easy.** Each is a clean, stationary, clearly-distinct spectrum. Real speech has coarticulation (the same "t" differs beside different vowels), speaker variation, and no clean stationary segments. Tiers 2–3 (more units, then real speech) are where it gets hard and may break.
- **The 5 was known.** k-means was told k=5. Discovering the *number* of units, not just sorting into a given number, is harder and untested.
- **This is still form, not meaning.** It found the *pieces* speech is made of. It has no idea what any of them refer to — that's rung 4, and it needs grounding (a second sense), which is unbuilt.

### Next

- **Tier 2:** more units (20+), add within-unit variation, and let the model discover the *count* (not hand it k). Find where segmentation and clustering start to smear.
- **Tier 3:** real speech (jfk.wav). Where do the boundaries land against the actual transcript? Qualitative, but the real test of whether any of this survives contact with reality.

---

## 015 — A slow level abstracts "what am I hearing" from real audio — speech vs music
*2026-07-16 · Audio · `SyntheticMind.Audio`, `SyntheticMind.Listen`, `ScenePerceptionTests`*

The keystone test: does a slow level find *meaningful* structure on *real* input, or only on the synthetic streams we designed to be discoverable? Alternated two real sources — JFK speech and a music clip — every 4 seconds, and asked whether the hierarchy forms a stable representation of which one is playing (a real, nameable slow variable; ground truth controlled).

```
  raw perception (mel) ~ source           0.78   (present, but jittery frame-to-frame)
  learned level-0 state ~ source          0.12–0.36   (the encoder throws it away)
  slow level over perception ~ source     0.80   (stable — a clean 'what am I hearing')
```

`dotnet run --project src/SyntheticMind.Listen -- jfk.wav music.wav` → **0.797**.

### The result

**A slow level forms a stable abstraction of "speech vs music" at 0.80 — with no labels.** Not a synthetic latent; a real, nameable property of real sound. The core thesis (a level discovers slow structure) holds on real input, and the thing it discovered is *interpretable*. That's the keystone.

### Two architectural findings, both real

1. **The learned encoder discards the source.** It's plainly in the perception (0.78), and level 0's max-variance encoder drops it to 0.12–0.36. Finding 008 — the encoder is blind to slow, mean-level structure — confirmed on real audio, not just synthetic. **So the slow level had to pool the PERCEPTION directly, not level 0's output.** Higher levels need access to perception, not only the learned encoder beneath them. (This is the "back-edge / multiple information sources" idea from [ARCHITECTURE.md §5](ARCHITECTURE.md), arrived at empirically.)

2. **The clock must match the timescale.** Pooling perception with too-slow integration (~5 s memory) blurred across the 4 s source segments → 0.34. Matching the integrator to the source (~2–3 s memory) → 0.80. Finding 010's rule, holding on real audio: a level's clock is a real parameter tied to the structure it hunts.

### Honest limits

- **The structure was imposed by me**, not found in the wild — I controlled the 4 s switching so there'd be ground truth. The model discovered *which* source, not *that there are sources*. Segmenting an unlabeled stream into its own regimes is the harder, untested next thing.
- **Source identity is an "easy" abstraction** — a gross mean-spectral difference (speech energy 23 vs music 19). Phonemes, words, musical structure are far subtler and remain untouched.
- The normalization that helps the encoder (gain control) *hurts* here — it subtracts the mean-spectral difference that carries the source. Turned off for this experiment; the tension between "normalize for stability" and "preserve slow structure" is unresolved and real.

### Next

- **Unsupervised segmentation.** Can a slow level partition a stream into regimes it wasn't told exist — no imposed switching? That's the real version of this.
- **Subtler abstractions.** Two speakers instead of speech-vs-music; verse-vs-chorus in one song. Where does "easy mean-difference" give way to something the model can't reach?

---

## 014 — It learns to predict real speech, on the fly, in eleven seconds
*2026-07-16 · Audio · `SyntheticMind.Audio`, `SyntheticMind.Listen`*

Fed a real recording — JFK's "ask not what your country can do for you" (whisper.cpp's `jfk.wav`, 16 kHz, 11 s) — through the cochlea into level 0, and watched its surprise (prediction error) over time. Added `WavReader` (any 16-bit / float WAV, any rate/channels → mono, resampled) so any file off the internet plays.

```
   time   loudness              surprise
   0.0s  #                     ###
   0.4s  #######               ########      ← very surprised at first
   0.8s  ##############        ###
   1.6s  ##########            #
   2.0s  (silence)             (nothing)     ← pause after "Americans"
   7.2s  #######               #             ← same loudness, barely surprised
   mean surprise: 1.034 (first third) → 0.309 (last third)
```

### Three things, and they're the whole thesis on real input

1. **It learned, unsupervised, in eleven seconds.** Mean surprise fell 3.3× across the clip. No labels, no training phase — it predicted the next moment of sound and got steadily less wrong.
2. **Surprise is novelty, not loudness.** 0.4 s and 7.2 s are *equally loud* (#######), but surprise went from ######## to #. The model reacts to the unexpected, not the loud — which is exactly what a predictive learner should do, and what a volume meter could never do.
3. **Silence is silent.** The pause after "Americans" and the other gaps show zero surprise — correctly nothing to predict.

The WAV reader and cochlea also just work: the loudness column traces the sentence's rhythm, silences and all.

### What this is, and what it is not

**Is:** the "learns on the fly, no labels, from raw input" claim, demonstrated on real sound rather than a synthetic stream. The plumbing (findings 001–013) carries real audio end to end.

**Is not:** understanding. This is *level 0* — the fast timescale. It learned the low-level statistics of this speaker's spectrum well enough to predict them. It did **not** learn phonemes, words, or meaning, and nothing here claims it did. Whether the slow levels, over much more speech, surface anything a human would name is still the open question — now askable with real data.

Two honest caveats: some of the early surprise is a cold start (the model has literally never heard anything), so the 3.3× includes warmup, not only speech-adaptation — though surprise falling to near-zero during *loud late speech* is genuine adaptation, not warmup. And it's one 11-second clip of one speaker.

### Getting here fixed a real bug

Surprise came out **NaN** at first: finding 013 made the *encoder* scale-free, but the readout and predictor inside the unit still had fixed rates and diverged on audio. Extended the same NLMS normalization to them. All 34 tests pass.

### Next

- **More audio, longer.** Minutes of speech or music, and watch whether a `TemporalLevel` over level 0 tracks anything structural (phrases, sections, speaker).
- **Run the live mic** (`dotnet run --project src/SyntheticMind.Listen`) — still the unrun real-world check; the file path (`-- file.wav`) is the same pipeline you can now drive deterministically.

---

## 013 — Self-scaling encoder: one learning rate for every input
*2026-07-16 · Predictive hierarchy · `SyntheticMind.Mind`, whole suite*

The rate/scale fragility surfaced in findings 008, 011, and 012 — every new input type needed the encoder's learning rate re-tuned by hand, or it diverged to NaN (which reads as a flat 0.000). This fixes it at the source.

**The fix:** normalize the encoder's step size by the input's own energy — NLMS (normalized least-mean-squares). The effective rate becomes `rate / (‖input‖² + 1)`, so a big input no longer means a big, unstable update.

```
  base rate 0.10, one value, three very different inputs:
    synthetic regime (±1, 2-band, nonlinear)   level 0 ~ regime   0.83
    audio pitch      (mel ~1.1, 20-band, linear) level 0 ~ pitch   0.88
    + downstream meta via the fixed slow level   L1 ~ meta          0.40
```

One rate now works across a ±1 synthetic signal and a ~1.1 twenty-band audio spectrum — the hand-tuning (0.005 here, 0.0001 there) is gone. The audio test and the live demo dropped their bespoke rates and use the default.

### Two things that mattered in getting it right

- **Instantaneous energy, not a running estimate.** A running energy estimate starts wrong and converges slowly; during that warmup the rate spikes and the weights diverge before the estimate catches up (worse for high-dimensional audio). Per-step ‖input‖² has no warmup. The `+1` floor keeps a near-silent frame from producing a huge step.
- **It shifted the fragile results, and that's honest.** Changing the core encoder moved the numbers on the two most fragile tests: the meta-meta at depth actually got *stronger* (0.40–0.56, was ~0.30), but the seed-fragile learned-level-1 (finding 007) recovered on a *different* set of seeds (0.01/0.32/0.09/0.17/0.62/0.46). Those tests hardcoded specific seeds; they now scan seeds and assert the phenomenon (best-seed works, spread = fragility) rather than a brittle exact number. The robust results (two-level, detection, readout, regime, audio) passed unchanged.

### Why this matters beyond tidiness

Every future input — a camera, a different sensor, a deeper stack — would have re-levied the same tax. Scale-invariance means the encoder now meets a new input without a human first guessing its magnitude. It's a small change (`rate / ‖input‖²`) that removes a recurring failure mode, which is the best kind.

### Next

Unchanged from 012: the live demo is still the first real-world check to run by hand, and whether the slow levels find *meaningful* structure in real speech is still the open question. The plumbing is just sturdier now.

---

## 012 — First real input: the hierarchy hears pitch through a cochlea
*2026-07-16 · Audio · `SyntheticMind.Audio`, `AudioPipelineTests`, `SyntheticMind.Listen`*

The synthetic streams did their job. This is the first real sensory input: sound, through a proper front-end, into the hierarchy that findings 001–011 built.

**The cochlea** (`Cochlea`, `Fft`): waveform → mel-band energies. Own iterative radix-2 FFT (tested: impulse is flat, a pure tone spikes at its bin), Hann window, mel-spaced triangular filters, log compression. It's the audio retina (SCAFFOLD.md §4) — fixed, learns nothing, just decodes frequency. Verified: a low tone lights a low band, a high tone a high band, silence is silent, louder is larger.

**The pipeline** (`AudioStream`): a real waveform whose pitch alternates (300 Hz ↔ 1200 Hz) rendered as actual samples, decoded by the cochlea, fed to a learned level 0. Result: **level 0 recovers the pitch at 0.79** — it hears which tone is playing, from samples, through the front-end. The whole machine works on real input.

### Three lessons the real input taught (all the same lesson, really)

Getting from "cochlea outputs 0.944 pitch signal" to "level 0 recovers it" took three fixes, and they rhyme with findings 008/011:

1. **The encoder needs gain control.** Raw mel energies run ~1.1 with a big positive offset, far from the synthetic ±1. At the usual rate the Sanger encoder diverged to NaN (→ 0.000, the now-familiar signature). Added adaptive normalization to `AudioStream` — running per-band standardization, exactly what real cochleas/retinas do. Its time constant must be slow (0.001) or it cancels the signal it's normalizing — the mean-tracking trap yet again.
2. **The learning rate must match the input scale.** Even normalized, the higher-dimensional mel input needed a much lower encoder rate (0.0001 vs 0.005). This keeps recurring; it should eventually be automatic (rate scaled by input variance), not hand-set.
3. **Don't add nonlinearity you don't need.** Quad features were *essential* for the synthetic frequency task (a nonlinear latent) and actively *harmful* here (pitch is linear in the mel spectrum): 0.79 with none, 0.37 with 64. The right nonlinearity is task-dependent, not a default.

### The live demo

`SyntheticMind.Listen`: microphone (NAudio) → cochlea → level 0 → a live display of the mel spectrum and level 0's **surprise** (prediction error), which should spike on sound onsets and settle during silence. Built and compiling; unverified live (no mic in the build environment) — it's the first thing to run by hand.

### Honest state

This proves *integration*, not *understanding*. Level 0 hears pitch — a fast, linear feature. Whether the slow levels, over real speech, discover anything *meaningful* (phonemes, words, a speaker's rhythm) rather than just "pitch-change rate" is completely unknown and is the real question now. The plumbing is real; whether the water is worth drinking is untested.

### Next

- **Run the live demo** and watch surprise track real sound. First qualitative reality check.
- **Real speech, real structure.** Feed recorded speech and ask whether the timescales the hierarchy surfaces line up with anything a human would name.
- The recurring rate/scale fragility (lesson 2) is now a tax on every new input type — worth fixing at the source (self-scaling encoder) before too much is built on top.

---

## 011 — Learn on top: a learned layer that uses the abstraction instead of erasing it
*2026-07-16 · Predictive hierarchy · `SyntheticMind.Lab`, `FrontierTests`*

The other half of path A. The fixed `TemporalLevel` produces a clean slow representation (finding 009); this closes the loop by putting a *learned* layer on top that uses it. The catch from finding 008: a learned max-variance ENCODER destroys slow structure (0.5 → 0.00). The resolution: what learns on top is a READOUT, not an encoder.

**The piece:** `LinearPredictor` — an online delta-rule linear predictor. Trained toward a target rather than toward variance, so it carries whatever predicts that target, slow structure included.

**The result** (readout on the slow level, predicting the next slow state):

```
  seed:                1      2      3      4      5      6
  prediction ~ meta:  0.524  0.424  0.462  0.533  0.542  0.579   (preserved — was 0.00 for the encoder)
  prediction quality: 1.00   1.00   1.00   1.00   1.00   1.00    (learned the slow dynamics)
```

The learned readout predicts the slow level's next state essentially perfectly, and its predictions **preserve the meta-regime at 0.42–0.58** — identical to the input representation. It uses the abstraction without erasing it, exactly where the max-variance encoder erased it to 0.00. That contrast is the whole point: *what learns on top of a slow representation must be a predictor, not a variance-seeker.*

### Two honest notes

- **Prediction quality 1.00 is partly free.** The slow state only changes once per pooling window (held constant between), so "predict next = current" is nearly right by default. The meaningful number is the preservation (0.42–0.58), not the quality.
- **The readout needs a learning rate matched to its input scale.** The slow state runs ~O(1); at rate 0.1–0.5 the delta rule diverged to NaN (which reads as 0 correlation — the bug that ate an hour of this session). At 0.005 it's stable. A real gotcha, worth the scar.

### Where path A stands, complete

Both halves are now built and robust:
- **Fixed temporal senses** (`TemporalLevel`: change-sensing + integration) produce a robust slow representation on every seed — findings 009 (two levels), 010 (composes to three).
- **Learning on top** (`LinearPredictor`) uses that representation without destroying it — this finding.

The architecture the project reached: a fast learned level that tracks the moment-to-moment cause, fixed temporal levels above it that expose progressively slower hidden causes, and learned readouts that build on those slow representations. Robust, tested, and understood.

### Next

The synthetic-stream phase has done its job — it proved the mechanism cleanly and cheaply. Real options now:
- **A real stream.** Point level 0 at something with genuine nested structure — audio (phonemes → words → prosody), or the earlier webcam idea — and see whether the timescales it discovers are meaningful rather than synthetic.
- **Close the top-down loop.** So far information only flows up. Let a slow level's representation bias the level below (attention/prediction), the missing back-edge from [ARCHITECTURE.md §5](ARCHITECTURE.md).
- **Path B, still optional.** Revisit whether a learned rule could replace the fixed `TemporalLevel` — now a research luxury, not a blocker.

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
