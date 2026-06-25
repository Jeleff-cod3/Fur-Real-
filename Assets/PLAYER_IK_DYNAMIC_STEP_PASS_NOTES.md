# PLAYER IK Dynamic Step Pass

Built from the last anticipation-pass archive. This pass focuses specifically on the failure where feet lift but keep trailing behind the body instead of traveling to a forward landing.

## Main code changes

Changed `Assets/Player/BodyIKTargets/AutoRunLegPairController.cs`.

### 1. Step landings are now predicted against the future hip/body position
The previous step endpoint path still clamped destinations against the leg start at the instant the step launched. At speed, that can erase the forward stride because the current hip cannot reach the future landing yet. The result is exactly the observed failure: the foot lifts, but the target stays behind.

New behavior:
- compute a predicted body/core position at landing time from current core velocity and step duration;
- compute a predicted leg-start/hip position for the same time;
- place the foot ahead of the predicted body, with side lane offset;
- clamp the real/planted endpoint against the predicted hip reach, not the current hip reach.

New fields:
- `usePredictedLandingReachForStepEnds`
- `predictedLandingTimeScale`
- `minimumPredictedLandingAheadReachRatio`

### 2. Active swing arcs no longer get clamped backward by current-frame reach
The active fake target used to be passed through the same current-reach clamp as planted targets. When a lifted arc was near reach limit, the clamp could pull the target backward instead of just reducing lift.

New behavior:
- active swing writes preserve the planned forward arc;
- if the lifted point would exceed current reach, only vertical lift is capped first;
- the forward travel is not projected backward during the swing.

New fields:
- `allowActiveSwingTargetPastCurrentReach`
- `capSwingLiftBeforeReachClamp`
- `activeSwingReachSlackRatio`

### 3. Gait timings/stride tuned for visible anticipatory steps
Prefab values adjusted:
- shorter active step duration at speed;
- higher foot target speed/frequency;
- larger forward landing ratio;
- earlier behind-body trigger;
- higher foot lift;
- emergency catch-up can start even if the other foot is stepping, so one blocked leg cannot stay stretched behind forever.

## Expected result
When moving forward, the rear foot should not just lift in place. The real target should land ahead of the body/hip's predicted landing position, and the fake target should travel through the arc toward that ahead point. Idle planted feet should still stay planted.

## First Unity validation
1. In play mode, watch each leg's `realTarget` and `fakeTarget`.
2. On a step start, `realTarget` should immediately appear ahead of `Node (4)` in the movement direction.
3. `fakeTarget` should move through the lifted arc toward that forward target.
4. If the foot lifts but still does not move forward, check whether another script/component writes the fake target transform after `AutoRunLegPairController.LateUpdate`.
