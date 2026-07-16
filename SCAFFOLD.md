# Scaffold — The Predictive Hierarchy

> Supersedes PLAN.md / ARCHITECTURE.md / SLICE-0.md, which described a different system
> (frozen encoder + lookup). That system is not this one. See §8.

---

## 1. The bet

**One operation, repeated: predict what comes next, learn from being wrong.**

Modality, hierarchy, and abstraction are *consequences* of that, not things we design. If we have to hand-build any of them, the bet is wrong and we should know early.

---

## 2. The unit

Every level is the same unit. There is no "encoder layer" and no "brain layer." Same circuit, different distance from the input.

```
              context (from above)
                     │
                     ▼
         ┌────────────────────────┐
         │         UNIT           │
         │   predict · compare    │──────► state (to above)
         │   learn  · publish     │
         └────────────────────────┘
                     ▲
                     │
              input (from below)
```

Four things, forever, on every tick:

1. **Predict** what's about to arrive from below — using its own state plus context from above
2. **Compare** the prediction to what actually arrived → error
3. **Learn** from the error, locally (no global backprop — that's the point)
4. **Publish** its state upward

No modality. No task. No labels. No training phase.

**Mountcastle's principle, as an engineering constraint:** if a unit ever needs to know what kind of data it's handling, we've broken the design. Sur's rewired ferrets grew visual cortex out of auditory cortex because the circuit doesn't care what you plug in. Ours shouldn't either.

---

## 3. The stack

**Level N's input is level N−1's *state*, not its raw signal.**

That's the JEPA move — predict in representation space, never in signal space. Its consequences:

- Each level predicts an *abstraction*, not a signal
- Higher levels see slower-changing input, so they represent slower-changing structure
- **Abstraction is forced, not designed.** Predicting one step ahead needs texture. Predicting far ahead makes texture useless and objects necessary. The hierarchy differentiates itself because the levels sit at different prediction horizons.

If we end up hand-assigning "this level does objects, this one does events," the bet failed.

---

## 4. The retina — fixed, dumb, and not part of the model

Humans don't predict pixels. They also don't *receive* pixels: the retina compresses ~100:1 into edges, contrast and motion before anything reaches cortex.

So each modality gets a **fixed, non-learned front end**:

| Modality | Front end |
|---|---|
| video | edges, contrast, motion — cheap classical filters |
| audio | spectra / cochlear-ish filterbank |
| text | token → vector, no semantics |

**This is not the "smart adapter" we rejected.** It's fixed, it's dumb, it learns nothing, it has no parameters worth mentioning. Its only job is to refuse to hand the model raw garbage. If it starts getting clever, it becomes the model and we've moved the problem instead of solving it.

---

## 5. Time is the only axis

No batch. No epoch. No train/test split. There's a stream and a clock.

- Training and inference are the same activity
- The system is never "done"
- "Trained on the fly" isn't a feature bolted on — there's no other mode

---

## 6. The five open decisions

These are the scaffold's holes. Each one is a real fork, not a detail.

| # | Decision | Why it matters |
|---|---|---|
| 1 | **What is a state, concretely?** Sparse binary hypervector? Dense float? Sparse distributed rep? | Determines the learning rule, the memory, and whether HDC's bind/bundle is even available to us |
| 2 | **What's the learning rule?** | No backprop is the whole thesis. Hebbian? Local error? This *is* the "new architecture," and it's the hardest part |
| 3 | **Does the level above receive state, or error?** | Classic predictive coding (Rao & Ballard) sends error up. JEPA sends representation. Genuinely different machines |
| 4 | **How do timescales separate?** | Fixed clock per level, or emergent? Emergent is the stronger claim and the bigger risk |
| 5 | **What does context-from-above actually do?** | Bias the prediction? Gate it? Select among competing predictions? |

---

## 7. The thing that will kill this

### Representational collapse

**The reason people predict pixels is that pixels can't lie.**

The moment a model predicts *its own representation*, there's a trivial winning move: make the representation constant. Output zero always, predict zero always, error is zero, learning complete. Perfect score. Zero information. A rock gets a perfect score.

This is not hypothetical or exotic. It is *the* central problem of every predict-in-latent-space method. BYOL, SimSiam, VICReg, DINO — look closely and most of their machinery is anti-collapse machinery: stop-gradients, EMA target networks, variance floors, covariance penalties. None of it is incidental.

> **So the price of "don't predict pixels" is "now you must prevent collapse."**
> That's the trade. It's worth taking. It isn't free.

**Decision 6, and it's really the only one that matters: what stops it collapsing?**

Any proposal for this architecture is, underneath, a proposal about how collapse is avoided. If we can't answer that, we don't have a design — we have a diagram.

---

## 8. What to build first

**Not video. Not text. Not the camera.**

The smallest thing that could show the primitive works:

- **One unit. One stream.** Something with real temporal structure but no perception problem — so that if it fails, we know it's the learning rule and not the front end.
- Candidates: a synthetic stream with hidden structure; a bouncing ball; a simple periodic signal with noise.

**Exit criteria — both, not either:**
1. It predicts better than chance *and* better than "assume nothing changes" (a shockingly strong baseline on real streams — beware)
2. **It hasn't collapsed.** Measure state entropy/variance directly. A model that "converges" beautifully and has collapsed looks identical to a model that works, from the loss curve alone.

**Then: two units stacked.** Does level 2 discover something slower than level 1, without being told to?

That single experiment is the whole thesis. Everything else is elaboration.

---

## 9. What this replaces

The existing code — `ClipEncoder`, `FastStore`, `Eye` — belongs to the rejected design: a frozen pretrained encoder with a lookup table in front. It learns nothing; it files things.

`FastStore` might survive as an associative memory later. The rest shouldn't anchor anything. It's ~500 lines and about two hours of work.

[FINDINGS.md](FINDINGS.md) 001 still stands, and it's now *evidence for this design*: a frozen encoder can only ever separate what its representation already separates. That's the ceiling this architecture exists to get past.

---

## 10. Rule formation — the reflex→rule bridge (far future, decided in principle)

The system so far only forms **reflexes**: wordless leans in the weights. It knows *an*-before-a-vowel in its bones and could never state why. We want the second kind of knowing too — patterns it can promote into **rules** that override the reflexes. This is the neural→symbolic bridge, and it's where most architectures in this family die (see the OpenCog note, §4-adjacent history in ARCHITECTURE.md).

The graveyard is one specific decision: **when does a hunch become a rule?** Too eager → superstition (coincidence carved into law). Too shy → permanent mush (never any rules at all). Nobody has a principled instant to pick.

**Decision (2026-07-16): there is no instant. A rule pays rent.**

- Noticing a pattern is cheap and commits to nothing.
- Every candidate rule then stays on probation *forever*. When it's active and prediction succeeds, it gains confidence and gets to speak louder; when it's active and prediction still fails, it loses confidence.
- A real pattern keeps paying rent and grows loud enough to boss the reflexes around. A coincidence stops paying almost immediately (it doesn't help predict the next case) and is quietly evicted — nobody ever *ruled* it wrong, it just stopped earning.
- A "rule" is therefore only ever *a hunch that has paid rent so long we let it override the reflexes* — and it can be demoted the day it stops earning.

**Why this fits:** it's the same engine as everything else — predict, check, adjust — turned on the machine's own hunches instead of on the input stream. No new mechanism, just a new target. Prediction error is already the currency; rent is paid in it.

**Prerequisite, stated honestly:** this is far off. There are no stable hunches to promote until the reflex layer works *in a hierarchy* — i.e. not until the §8 abstraction question (finding 002/003's open thread) is answered. Recorded now so the principle survives; not to be built next.
