# Findings

The experiment log. One entry per real result, newest first. Numbers with dates, not impressions.

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
