---
name: maui-design-from-image
description: Convert a UI design image (screenshot, Figma export, AI-generated mockup, client-supplied PNG) into production-ready .NET MAUI XAML — extracting design tokens, building a ResourceDictionary, generating reusable ContentView components, and assembling the full page. Use this skill whenever the user uploads or references a UI design, mockup, screenshot, wireframe, or "make this in MAUI" request, and also when they ask to recreate an app screen, build a MAUI page from a picture, convert a design to XAML, match a client design, or replicate a UI layout — even if they never say the words "MAUI" or "XAML" but the project context is .NET MAUI. Also triggers for follow-up work on the same design — adding more screens, refining spacing, matching colors, or fixing fidelity gaps.
---

# Design image → .NET MAUI XAML

Turn a picture of a UI into XAML that a developer can build on. The goal is not a
one-shot dump of markup — it is a **token layer, a component layer, and a page layer**,
in that order, so the second screen costs a fraction of the first.

## Why this order matters

Most attempts at "image → XAML" fail because they inline every color and size into one
giant page. Then screen two arrives, nothing is reusable, and the developer rewrites
everything. Extracting tokens first and components second means an eight-screen design
system takes roughly the effort of two screens.

---

## Workflow

### Step 1 — Read the image and write a Design Report

Before writing any XAML, describe what is actually in the picture. Produce this exact
structure so the user can correct misreadings cheaply, before code exists:

```markdown
## Design report

**Screen:** <name / ID if visible in the image>
**Window:** <size ratio, corner radius, custom or native titlebar>

### Palette
| Token | Hex | Where it appears |

### Typography
| Role | Size | Weight | Color | Example text |

### Layout skeleton
<a Grid/StackLayout tree in plain words, with row/column proportions>

### Repeated elements
<list every visual pattern appearing 2+ times — these become ContentViews>

### Raster assets needed
<illustrations, logos, 3D renders — these are images, NOT XAML>

### Fidelity risks
<anything MAUI cannot do natively — see references/fidelity-gaps.md>
```

**Estimate hex values honestly.** Reading colors off a compressed screenshot is
approximate. Say so, and mark values as estimates so the user knows to confirm against
the source file. If the design came from Figma, ask for the token export instead of
guessing.

**Separate raster from vector.** 3D illustrations, photographic elements, and complex
gradients-with-noise are image assets. Attempting them in XAML produces bad output and
wastes hours. Call them out in the report and reference them as `<Image Source="..."/>`.

### Step 2 — Confirm before building

Show the design report and ask two questions:
1. Do the colors and font look right, or do you have the original design file?
2. Which screen should I build first?

Skip this confirmation only when the user has explicitly said "just build it".

### Step 3 — Generate the token layer

Write `Resources/Styles/Colors.xaml` and `Resources/Styles/Styles.xaml`.
Start from `assets/Colors.xaml` and `assets/Styles.xaml` in this skill and replace the
values with the ones extracted in Step 1. Never inline a hex code in a page when a token
exists.

Token naming stays semantic, not literal — `PrimaryBrand` rather than `Blue600`, because
when the client changes the brand color the name should still make sense.

### Step 4 — Generate the component layer

Every pattern listed under "Repeated elements" becomes a `ContentView` with `BindableProperty`
declarations. Read `references/component-recipes.md` for worked implementations of the
patterns that show up in nearly every dashboard design: stat tiles, status pills, glass
cards, gradient buttons, timers, summary strips, and confirmation dialogs.

A component is worth extracting when it appears twice or more, or when it carries logic
(a status pill that changes color by state). A one-off decorative element stays inline.

### Step 5 — Assemble the page

Compose the page from components. The page file should read like the design report's
layout skeleton — mostly structure, very little styling. If a page file is full of
`BackgroundColor="#FFFFFF"` and `CornerRadius="18"`, the token and component layers were
skipped and the work needs redoing.

Read `references/xaml-patterns.md` for the layout constructs — Grid sizing, Border with
StrokeShape, Shadow, LinearGradientBrush, and the MAUI equivalents of common CSS idioms.

### Step 6 — Self-check before handing over

Two failure modes account for most broken deliverables, and both are invisible until
build time. Check for them explicitly:

**Undefined resource keys.** Every `{StaticResource X}` written in a page or component
must exist in `Colors.xaml` or `Styles.xaml`. A single missing key throws at page load —
MAUI does not fail gracefully here. Scan the generated files and reconcile the two lists
before delivering. This is the most common defect in generated XAML because a plausible
key name is easy to invent mid-file.

**Invented icon codepoints.** Never write a specific glyph like `&#xe8b5;` as if it were
verified — codepoints differ between icon fonts and between versions of the same font,
and a wrong one renders as a blank box or an unrelated symbol. Instead, emit a named
constant and hand the user a glyph inventory to fill in:

```xml
<!-- Resources/Styles/Icons.xaml -->
<x:String x:Key="IconCalendar">&#xEBCC;</x:String>   <!-- VERIFY against your icon font -->
<x:String x:Key="IconClock">&#xE8B5;</x:String>      <!-- VERIFY -->
```

Then list every icon the screen needs, by role, so the user can map them once:

| Role | Resource key | Where it appears |
|---|---|---|
| calendar | `IconCalendar` | Today's Date tile |

### Step 7 — Report the fidelity gaps and the handover checklist

End every build by telling the user, concretely, what will not look identical and what
the options are. `references/fidelity-gaps.md` catalogs the known ones. Being upfront
here prevents the far worse outcome of a client review where the gaps are discovered
by surprise.

Then close with the handover checklist — XAML files alone never compile, and leaving
these implicit guarantees a frustrated first build:

```markdown
## Before this builds, add:
- [ ] Font files: <exact .ttf filenames, one per weight> → Resources/Fonts/
- [ ] Font registrations in MauiProgram.cs: <the AddFont lines>
- [ ] Merged dictionaries in App.xaml: <Colors.xaml, Styles.xaml, Icons.xaml — in that order>
- [ ] Raster assets: <filenames, transparent PNG, 2x and 3x>
- [ ] Icon glyph codepoints verified: <count> icons — see the inventory table
- [ ] NuGet packages: <any needed, e.g. H.NotifyIcon.WinUI for tray>
- [ ] Platform code: <window sizing, titlebar, backdrop — see windows-platform.md>
```

---

## Platform notes

Desktop MAUI targets — custom titlebars, tray icons, Mica/Acrylic backdrops, window
sizing — need platform-specific code that lives outside XAML. When the design shows a
custom window chrome (rounded corners, custom minimize/close buttons, no native
titlebar), read `references/windows-platform.md`.

---

## Output structure

Deliver files in this layout so they drop straight into a MAUI project:

```
Resources/
├── Styles/
│   ├── Colors.xaml          ← token layer
│   └── Styles.xaml          ← implicit + named styles
├── Fonts/                   ← note which .ttf files the user must add
└── Images/                  ← note which raster assets are needed
Controls/
├── GlassCard.xaml(.cs)      ← component layer
├── StatTile.xaml(.cs)
├── StatusPill.xaml(.cs)
└── ActionButton.xaml(.cs)
Pages/
└── <ScreenName>Page.xaml    ← page layer
```

Always state which `App.xaml` merged-dictionary entries and which `MauiProgram.cs` font
registrations the user needs to add, since XAML files alone will not compile without them.

---

## Working style

**When several screens arrive at once, inventory before building.** With a multi-screen
design set, the most valuable first output is not a page — it is a table of every
component, which screens use it, and which variants each screen demands. Build order then
falls out naturally: the component used by seven screens gets built first and gets the
most care. Building screen 1 in isolation and discovering on screen 4 that the stat tile
needs a centered no-icon variant means reworking every earlier page.

```markdown
| Component | Screens | Variants needed |
|---|---|---|
| StatTile | 1,2,3,5,6,7,8 | icon-left; centered no-icon; with caption |
| GlassCard | all | default; inner (no shadow); dialog |
```

**Design components for the variants you found, not just the first occurrence.** A tile
that only supports icon-left will be copy-pasted and mangled the moment a centered one is
needed. Add an `IconPosition` or `ShowIcon` bindable property up front — it costs three
lines then, and a refactor later.

**Build one screen completely before starting the next.** A half-finished set of eight
screens is worth less than one screen a developer can run, see, and react to.

**Prefer `Grid` over nested `StackLayout`.** Deeply nested stacks are the most common
cause of MAUI layout performance problems and make responsive behavior harder to reason
about. Use `Grid` with proportional (`*`) and auto sizing.

**Write `x:Name` only where code-behind or bindings need it.** Named elements that
nothing references are noise.

**Give real sample values in the XAML.** A design shows `05:06:59` in the timer — put
that in as the default so the page renders meaningfully in the previewer instead of
showing empty boxes.

**When the design is web-native, say so.** Heavy glassmorphism, backdrop blur, gradient
text, and animated glow are CSS idioms. They are achievable in MAUI only with
approximations or SkiaSharp. If the user has flexibility on stack, mentioning
`MAUI Blazor Hybrid` or `WPF + WebView2` once is honest and useful. If they have already
committed to native XAML, drop it and build the best approximation without relitigating.

---

## Reference files

| File | Read when |
|---|---|
| `references/xaml-patterns.md` | Writing any layout — CSS→XAML mapping, Grid, Border, Shadow, brushes |
| `references/component-recipes.md` | Building the component layer — full ContentView implementations |
| `references/windows-platform.md` | Design shows custom window chrome, tray icon, or Mica backdrop |
| `references/fidelity-gaps.md` | Writing the design report and the closing gap summary |
| `assets/Colors.xaml`, `assets/Styles.xaml` | Starting the token layer — copy and edit |
| `assets/design-tokens.schema.json` | User wants tokens as machine-readable JSON |
