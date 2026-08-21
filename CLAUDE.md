# Wilo Project Overview

This is the target design, not a build checklist. Implement only what the current task needs. Do not create systems, classes, components, or attributes just because they are described here.

Use clear but concise comments where they materially aid developer understanding. Favor simple, modular systems that can expand later.

## 1. Game Concept

**Wilo** is a cozy, stylized lake-life and bass-fishing game set around the player's cabin on **Wilo Lake**.

The game combines the charm and warmth of games like Animal Crossing with a genuinely deep fishing simulation.

The player lives at and gradually improves a cabin on the lake, fishes recreationally, prepares for periodic tournaments, upgrades equipment and boats, and learns how bass behave under changing environmental conditions.

The fishing should feel approachable and playful visually while rewarding real understanding of:

* Season
* Water temperature
* Weather
* Wind
* Time of day
* Depth
* Structure
* Vegetation
* Forage
* Lure choice
* Presentation

Core loop:

**Wake at cabin → observe conditions → prepare → fish / scout → learn lake → improve cabin and equipment → compete in tournaments → repeat**

The emotional goal is:

**Build your dream life on the lake while becoming an expert at understanding Wilo Lake.**

---

## 2. Design Pillars

### Cabin

The cabin is the player's home and progression hub.

The player may:

* Decorate and furnish it
* Improve or expand it
* Upgrade the dock/property
* Display trophies and memorable catches
* Store and organize fishing equipment
* Purchase or display boats and gear

The cabin should gradually become a physical record of the player's accomplishments.

Cabin gameplay should remain relatively lightweight and should not become a complex construction or survival simulator.

### Lake

Wilo Lake should feel like a place the player genuinely learns over time.

Important lake features may include:

* Shorelines
* Flats
* Points
* Drop-offs
* Creek channels
* Vegetation
* Rock
* Timber
* Docks
* Deep-water structure

One highly detailed, learnable lake is more important than many shallow lakes.

### Fishing

Fishing is the deepest gameplay system and primary source of mastery.

Success should come from understanding where bass are likely to be and what they are likely to bite under current conditions.

The player should gradually develop personal knowledge of the lake rather than simply being shown optimal fishing locations.

---

## 3. Fishing Simulation

Wilo does **not** need to simulate hundreds of persistent individual bass moving around the lake.

Instead, the lake is represented by an underlying probabilistic fishing model.

At any location and under any conditions, the simulation should be able to determine concepts such as:

* Probability that catchable bass are present
* Probability of a strike
* Fish size distribution
* Fish activity / aggression
* Suitability of the selected lure and presentation

A cast should query the lake and environmental conditions to determine fishing outcomes.

Conceptually:

**Habitat suitability × seasonal behavior × current conditions × fish activity × lure suitability × presentation → strike probability**

A successful strike then samples from the local fish-size distribution to create the hooked fish.

For example, one location may produce frequent small fish while another produces fewer bites but a greater probability of large bass.

This distinction is important, particularly during tournaments.

### Cast Path

A cast should eventually be capable of evaluating the lure's path through the lake rather than treating the landing point as the only relevant location.

For example:

**shallow flat → vegetation → weed edge → drop-off**

Strike probability may change throughout the retrieve, allowing players to discover patterns such as fish consistently biting at a weed edge or depth transition.

### Presence vs. Bite Probability

Where practical, distinguish:

**Fish presence / density**

from:

**Fish willingness to bite**

Fish may be present but inactive because of conditions or poorly matched lure choice.

This enables authentic situations where the player must determine both:

1. Where the fish are
2. How to catch them

### Hidden Simulation

Do not expose raw probability values to the player during normal gameplay.

The simulation should be understood indirectly through:

* Weather
* Water temperature
* Sonar
* Lake contours
* Visible habitat
* Previous catches
* Fishing journal/history
* Seasonal patterns
* Observed forage activity

The goal is for the player to think:

**"I think I know where the fish should be."**

---

## 4. Time, Weather, and Seasons

Environmental conditions are central to the fishing simulation.

Potential state includes:

* Date
* Time of day
* Season
* Air temperature
* Water temperature
* Cloud cover
* Rain
* Wind direction
* Wind speed
* Water clarity

These conditions should influence fishing behavior in understandable but not perfectly predictable ways.

Seasonal progression may include:

* Prespawn
* Spawn
* Postspawn
* Summer
* Fall feeding
* Winter

The world should visually reinforce changing conditions and seasons.

---

## 5. Tournaments

Fishing tournaments provide structure and stakes without dominating every day.

A tournament may occur approximately once per simulated week.

Non-tournament days are valuable preparation days used to:

* Scout areas
* Test lure patterns
* Learn current fish behavior
* Improve equipment
* Prepare tackle
* Work on the cabin/property

Tournament formats may include:

* Best five bass by total weight
* Largest individual bass
* Special challenges

Tournament conditions should use the same fishing simulation as normal gameplay.

Tournament success should therefore reward actual knowledge gained during practice.

The tournament morning should feel meaningfully different from an ordinary fishing day.

---

## 6. Progression

Progression should primarily represent improvement to the player's lake life rather than traditional character leveling.

Possible progression includes:

### Fishing

* New rods
* Reels
* Lures
* Tackle
* Electronics / sonar
* Boat upgrades
* New fishing techniques

### Cabin / Property

* Furniture
* Cabin expansions
* Porch
* Fireplace
* Workshop
* Improved dock
* Tackle storage
* Trophy displays
* Landscaping

### Accomplishments

* Personal-best fish
* Lake records
* Tournament wins
* Trophy catches

Important catches and tournament achievements should be representable physically within the cabin where possible.

---

## 7. Multiplayer / Social

Multiplayer may be added in the future; do not introduce complexity for it unless needed.

The vision: multiple players share the same lake, living and fishing together as neighbors, including in tournaments.

---

## 8. Art Direction

Wilo should have a **stylized, animated, cozy visual identity**, not a photorealistic fishing-simulator aesthetic.

Target feeling:

* Warm
* Charming
* Peaceful
* Slightly playful
* Beautiful miniature world
* Expressive rather than physically exact

Potential inspiration includes the accessibility and charm of games such as Animal Crossing, while maintaining Wilo's own distinct visual identity.

The game should feel welcoming even to players who know nothing about fishing.

The underlying fishing simulation can be deep without the visual presentation feeling technical or serious.

A useful design principle is:

**Cute outside. Deep inside.**

---

## Technical Approach

### General

* Engine: **Unity 6**
* Language: **C#**
* Rendering: **Universal Render Pipeline (URP)**
* Keep gameplay logic modular and testable.
* Favor data-driven fishing and environmental systems.
* Keep visual presentation separate from underlying simulation where practical.
* Confirm major architectural decisions before introducing significant new abstractions.
* After changes, summarize what changed and provide concise in-editor test steps.

### Unity Architecture

Use Unity concepts deliberately:

* `MonoBehaviour` for scene-bound behavior
* Plain C# classes for simulation and logic that do not require Unity lifecycle or scene access
* `ScriptableObject` for reusable/configurable game data where appropriate
* Prefabs for reusable scene objects
* Components/composition instead of deep inheritance where practical
* Serialized fields for designer-configurable values

The fishing simulation should preferably be implemented as plain, testable C# logic rather than being tightly coupled to GameObjects.

Avoid expensive per-frame simulation when systems can instead update:

* On environmental changes
* At scheduled intervals
* During casts
* Through cached or derived probability data

Do not create persistent individual fish agents unless a specific gameplay or visual feature requires them.

A hooked fish may become an instantiated gameplay object only after the simulation determines that a strike occurred.

---

## Unity / MCP

When Unity MCP or equivalent editor tooling is available, use it to inspect and interact with the actual Unity project rather than guessing about scene, prefab, component, or editor state.
