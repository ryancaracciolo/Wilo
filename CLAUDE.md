# Wilo Project Overview

## Project notes

Gameplay depth is `WorldConditions.gameplayDepthScale` (default 0.4): sonar, map colors, and fishing use scaled depth; fish still sit on the visual lake bed — do not reshape the terrain to make the lake shallower.

This is the target design, not a build checklist. Implement only what the current task requires. Do not create systems, classes, components, or attributes solely because they are described here.

Favor simple, modular systems that can expand later. Use concise comments only where they materially improve understanding.

## 1. Game Concept

**Wilo** is a cozy, stylized lake-life and bass-fishing game set around the player's cabin on **Wilo Lake**.

It combines the warmth and accessibility of games like Animal Crossing with a genuinely deep fishing simulation.

The player lives on the lake, improves their cabin and property, fishes recreationally, scouts for tournaments, upgrades equipment and boats, and gradually learns how bass respond to changing conditions.

Fishing should reward understanding of:

* Season
* Water temperature
* Weather and wind
* Time of day
* Depth
* Structure and vegetation
* Forage
* Lure choice
* Presentation

Core loop:

**Wake at cabin → observe conditions → prepare → fish / scout → learn lake → improve life and equipment → compete → repeat**

Emotional goal:

**Build your dream life on the lake while becoming an expert at understanding Wilo Lake.**

---

## 2. Design Pillars

### Cabin

The cabin is the player's home and progression hub.

Possible activities include:

* Decorating and furnishing
* Cabin/property upgrades
* Improved docks and storage
* Purchasing/displaying boats and gear

The cabin should become a physical record of progress without becoming a complex construction or survival game.

### Lake

Wilo Lake should feel like a place the player genuinely learns.

Important features may include:

* Shorelines and flats
* Points and drop-offs
* Creek channels
* Vegetation
* Rock and timber
* Docks
* Deep-water structure

One highly detailed, learnable lake is more important than many shallow ones.

### Fishing

Fishing is the deepest gameplay system.

The player should learn:

1. Where fish are likely to be
2. How active they are
3. What they may bite
4. How to present the lure

The game should reward accumulated knowledge rather than reveal optimal answers.

---

## 3. Fishing Simulation

Wilo should **not** simulate thousands of persistent individual bass across the lake.

Use two primary representations:

1. **Lake-wide habitat / probability data**
2. **Physical fish near the player**

Conceptually:

**Habitat model → local fish population → physical fish → lure interaction → bite / rejection**

### Lake-Wide Distribution

The entire lake has an underlying spatial model describing expected fish distribution.

It may consider:

* Species
* Depth
* Bottom composition
* Vegetation / cover / structure
* Temperature and season
* Time of day
* Weather and wind
* Forage
* Water clarity

For a given area, the simulation should derive concepts such as:

* Expected species density
* Fish size distribution
* Activity / aggression
* Habitat suitability

This model is the source of truth for **where fish should be**.

It should remain cheap data, not persistent fish GameObjects.

### Local Physical Fish

When the player approaches an area, nearby lake cells may instantiate actual fish based on the underlying model.

Target active distance is approximately **two full casts**, with additional spawn/despawn buffers so transitions are not visible.

Physical fish may:

* Swim within nearby habitat
* Associate with cover
* React to boat/player disturbance
* Detect lures
* Follow, reject, or strike

Fish should originate from habitat data, not spawn arbitrarily around the player.

Prefer activating lake cells intersecting the player's radius so fish remain associated with locations in the lake.

Use pooling or another lightweight lifecycle strategy where appropriate.

### Persistence

The initial system does not require persistent individual fish across the whole lake.

Local populations may be generated from habitat, density, size distribution, and current conditions.

Deterministic seeds or lightweight state may be used so revisiting an area does not obviously reroll every fish.

Only build persistent individual fish identities or migration if later gameplay specifically requires them.

### Visibility

Physical fish are not automatically visible.

Visibility may depend on:

* Depth
* Water clarity
* Vegetation
* Lighting
* Viewing angle
* Distance

Players should occasionally spot fish in believable situations such as shallow water, weed edges, docks, spawning areas, or near the surface.

Sight-fishing should be possible, but good water should not look like an aquarium.

### Lure Interaction

Nearby physical fish should drive fishing interactions.

Conceptually:

**Lure enters awareness range → fish notices or ignores → interest → follow / reject → strike**

Response may eventually consider:

* Activity
* Species and size
* Lure type, color, and size
* Retrieve speed/depth
* Presentation
* Water clarity
* Weather / time
* Disturbance

Keep outcomes probabilistic enough that fishing never becomes perfectly deterministic.

### Cast Path

Fishing should eventually evaluate the lure across its retrieve path, not only at the landing point.

Example:

**flat → vegetation → weed edge → drop-off**

This enables patterns such as fish consistently striking at a depth transition or following a lure out of cover.

### Presence vs. Bite

Keep these separate:

**Fish presence / density**

vs.

**Fish willingness to bite**

Fish may be present but inactive, or may notice and reject a lure.

The player must solve both:

1. Where the fish are
2. How to catch them

### Hidden Simulation

Do not expose raw probabilities during normal gameplay.

Teach the lake through:

* Weather and water temperature
* Sonar and contours
* Visible habitat/fish
* Previous catches
* Fishing journal/history
* Seasonal patterns
* Forage activity

The intended player thought is:

**"I think I know where the fish should be."**

---

## 4. Environment

Environmental state may include:

* Date / time
* Season
* Air and water temperature
* Cloud cover / rain
* Wind direction / speed
* Water clarity

These should influence fish distribution, activity, and lure response in understandable but imperfectly predictable ways.

Seasonal phases may include:

* Prespawn
* Spawn
* Postspawn
* Summer
* Fall feeding
* Winter

Visual conditions should reinforce the simulation.

Prefer gradual, scheduled, or area-based simulation updates over unnecessary per-frame recalculation.

---

## 5. Tournaments

Tournaments provide periodic structure and stakes.

Non-tournament days are useful for:

* Scouting
* Testing lure patterns
* Learning current conditions
* Improving equipment
* Preparing tackle
* Working on the cabin/property

Possible formats include:

* Best five bass by total weight
* Largest bass
* Special challenges

Tournament fishing must use the same simulation as normal fishing so practice and lake knowledge matter.

---

## 6. Progression

Progression represents improvement to the player's lake life rather than traditional leveling.

### Fishing

Possible progression:

* Rods / reels
* Lures / tackle
* Electronics / sonar
* Boat upgrades
* New techniques

Upgrades should create options and capability without replacing player knowledge.

### Cabin / Property

Possible progression:

* Furniture
* Cabin expansions
* Porch / fireplace / workshop
* Improved dock
* Storage
* Trophy displays
* Landscaping

### Accomplishments

* Personal-best fish
* Lake records
* Tournament wins
* Trophy catches

Important achievements should be physically representable in the cabin where practical.

---

## 7. Multiplayer

Multiplayer may be added later.

Long-term vision may include players sharing the same lake as neighbors and competing in tournaments.

Do not introduce multiplayer complexity unless specifically requested.

---

## 8. Art Direction

Wilo should feel **stylized, animated, cozy, and low-poly**, not photorealistic.

Target feeling:

* Warm
* Charming
* Peaceful
* Slightly playful
* Miniature-world quality
* Expressive rather than physically exact

Favor:

* Clear silhouettes
* Simplified shapes
* Slightly exaggerated proportions
* Broad colors
* Limited texture detail
* Strong readability

Avoid photorealistic fish, characters, or overly detailed PBR assets.

Assets from different sources should be adapted to a coherent visual language.

Design principle:

**Cute outside. Deep inside.**

---

# Technical Approach

## General

* Engine: **Unity 6**
* Language: **C#**
* Rendering: **URP**
* Keep systems modular and testable.
* Favor data-driven fishing/environment systems.
* Separate visual presentation from simulation where practical.
* Confirm major architectural decisions before adding significant abstractions.
* After changes, summarize what changed and provide concise test steps.

## Unity Architecture

Use Unity concepts deliberately:

* `MonoBehaviour` for scene-bound behavior
* Plain C# for simulation and logic
* `ScriptableObject` for reusable/configurable data where appropriate
* Prefabs for reusable scene objects
* Composition over deep inheritance
* Serialized fields for designer-configurable values

The lake-wide fishing model should preferably be plain, testable C#.

Physical fish are appropriate within the player's local active area.

Core performance model:

**Whole lake = cheap habitat/probability data**

**Near player = limited physical fish**

Avoid expensive per-frame work when updates can occur:

* When entering/leaving lake areas
* At scheduled intervals
* When environmental conditions materially change
* During lure/fish interactions
* Through cached habitat data

Physical fish should use lightweight local behavior and should not perform whole-lake calculations every frame.

Do not add a virtual-individual-fish layer unless future gameplay specifically requires persistent fish identities or migration.


---

## Unity / MCP

When Unity MCP or equivalent editor tooling is available, use it to inspect and interact with the actual Unity project rather than guessing about scene, prefab, component, or editor state.