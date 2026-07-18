# CSI Modelling Tools

CSI Modelling Tools is a Windows WPF desktop application for CSI ETABS and SAP2000 modelling workflows. It helps structural engineers generate parametric structures, manage ETABS model data, compare ETABS model revisions, import structural IFC data into ETABS, and run focused validation/analysis helpers before writing changes back to CSI products.

The application targets .NET 8 on Windows, runs as a 64-bit WPF app, uses CSI COM/API assemblies from the local `lib/` folder, uses HelixToolkit for 3D previews, and uses XBIM for IFC parsing.

## What The Tool Does

- Connects to running ETABS instances and reads frame sections, shell properties, materials, load patterns, load combinations, stories, groups, frames, areas, joints, releases, restraints, and property data.
- Connects to running SAP2000 instances for the City of Tomorrow workflow.
- Generates ETABS parametric trusses, arrays of trusses, orthogonal Y-Z truss systems, spiral staircases, fish-belly trusses, variable-panel trusses, domes, plate girders, retaining walls/drains, and railings.
- Generates a symmetrical Vierendeel "City of Tomorrow" structure into ETABS or SAP2000, including frame members and tension-only cable/tie members.
- Imports existing ETABS frame objects for bulk section reassignment, group assignment, and distributed-load updates.
- Manages ETABS load patterns, static load cases, response combinations, materials, frame sections, slab/wall properties, imported steel sections, and tapered nonprismatic steel sections.
- Compares two ETABS models or saved ETABS snapshots and reports added, removed, moved, modified, and unchanged objects.
- Imports structural elements from IFC files, exports review JSON/Markdown reports, and optionally exports recognised frames/slabs/walls to ETABS.
- Provides live 2D/3D previews, validation messages, warnings, progress bars, review tables, and selection helpers before model-changing operations.

## Requirements

- Windows.
- .NET 8 SDK or newer. `global.json` requests SDK `8.0.100` with `latestMajor` roll-forward.
- 64-bit runtime/build environment. The project sets `PlatformTarget` to `x64`.
- CSI ETABS installed for ETABS-connected features.
- CSI SAP2000 installed for SAP2000-connected City of Tomorrow features.
- Local CSI API files in `lib/`:
  - `ETABSv1.dll`
  - `CSiAPIv1.dll`
  - `SAP2000v1.dll`
  - related `.deps.json`, `.runtimeconfig.json`, `.comhost.dll`, `.tlb`, and `.pdb` files where present
- NuGet packages:
  - `HelixToolkit.Wpf`
  - `Xbim.Essentials`
- Steel database file:
  - `PropertyLibraries/BSShapes2006.xml`

The project file validates the required CSI DLLs before build. Some local preview and generation logic can run without ETABS/SAP2000 open, but read/write commands need a running CSI product under the same Windows user/elevation as this app.

## Build And Run

From the repository root:

```powershell
dotnet restore "CSI Modelling Tools.slnx"
dotnet build "CSI Modelling Tools.slnx"
dotnet run --project "CSI Modelling Tools.csproj"
```

To run from Visual Studio, open `CSI Modelling Tools.slnx`, restore packages, build, and start the WPF project.

## General CSI Connection Workflow

For ETABS tabs:

1. Open the target ETABS model.
2. Start CSI Modelling Tools.
3. Click `Refresh ETABS Instances`.
4. Select the intended ETABS instance.
5. Click the tab-specific read command such as `Read ETABS Data`, `Read Load Data`, or `Read Properties`.
6. Enter geometry, section, load, import, or comparison inputs.
7. Review previews, warnings, and validation messages.
8. Use `Validate`, `Preview`, `Compare`, `Run Import`, or the relevant apply/export command.
9. Save the ETABS model after successful changes.

For SAP2000 City of Tomorrow:

1. Open SAP2000.
2. Use the `SAP2000 Connection` panel in the `City of Tomorrow` tab.
3. Click `Refresh SAP2000 Instances`, then `Read SAP2000 Sections`.
4. Assign frame/cable/tendon sections.
5. Generate, regenerate, or clear the SAP2000 structure.

## Feature Guide

### ETABS Parametric Modelling

The `ETABS Parametric Modelling` tab generates truss-like frame systems and sends them to ETABS.

Supported types:

- Warren.
- Warren without verticals.
- Inverted Warren.
- Inverted Warren without verticals.
- Pratt.
- Howe.
- K truss.
- Simple frame.
- Line frame only.
- Spiral staircase.
- Fish-belly truss.
- Variable-panel-width truss.

Standard truss features:

- Manual span or two selected ETABS insertion points.
- Horizontal, vertical, or any-angle orientation.
- Base X/Y, top Z, story-based Z reference, and truss angle inputs.
- Multiple truss parameter rows with add, copy, and remove commands.
- Preview color per truss parameter set.
- Truss spacing input with automatic perpendicular offset or global X/Y/Z offset.
- Y bay count and Y bay spacing to duplicate the X-Z truss across multiple Y lines.
- Optional orthogonal Y-Z trusses between bay lines.
- Orthogonal Y-Z type selection, including same-as-XZ or explicit truss type.
- Orthogonal Y-Z placement at every panel point, end lines only, or every other panel point.
- Configurable Y-Z panels per bay.
- Top and bottom chord slope controls.
- Frame section assignments for top chord, bottom chord, diagonal, vertical, end post, and secondary members.
- Support-node mode: end bottom nodes, all bottom chord nodes, or no supports.
- Support restraint mode: first pin/others roller, all pinned, or all Z rollers.

Special truss features:

- Spiral staircase geometry: center, base Z, total height, inner radius, outer radius, step count, total rotation, start angle, and direction.
- Spiral staircase member toggles for inner stringer, outer stringer, radial tread beams, tread shell plates, central column, top landing beam, and bottom landing beam.
- Fish-belly truss geometry: start point, span, panels, end depth, middle depth, direction angle, top chord slope, bottom chord shape, and web pattern.
- Variable-panel truss geometry: start point, span, panels, depth, end/middle panel-width ratios, direction angle, width variation, and web pattern.
- Fish-belly and variable-panel same-section or per-member section assignment.
- Optional moment releases for truss behavior.

Load features:

- Add multiple load definitions.
- Target top chord or bottom chord.
- Enter line load in kN/m or area load in kPa with panel width.
- Apply loads as panel-node loads or ETABS member line loads.
- Remove individual loads or clear all loads.

ETABS export and checking:

- Export mode: erase/redraw or add as new with X/Y/Z offset.
- Overlap draw mode: current behavior, force duplicate coincident joints, or share overlapping joints.
- Validate generated geometry and assignments before export.
- Send generated frames/shells to ETABS.
- Crash check generated truss members against existing ETABS frames.
- Select same-line crash frames back in ETABS for review.

### City of Tomorrow

The `City of Tomorrow` tab generates a symmetrical Vierendeel-style structure for ETABS or SAP2000.

Geometry inputs:

- Structure ID.
- Clear span.
- Panels per half.
- Vierendeel depth.
- Bottom chord level.
- Mid-rail ratio.
- Tie level.
- External anchor width.
- External side-frame height.
- Pile-cap level.

Member groups:

- Top chord.
- Intermediate rail.
- Bottom chord.
- Vertical posts.
- Towers.
- Side frames.
- Internal cable fans.
- External backstays.
- Global lower tie.
- Pile/foundation support nodes.

ETABS features:

- Reads ETABS frame sections.
- Generates a new ETABS model structure or regenerates an existing generated group.
- Clears the generated ETABS structure.
- Draws tension-only members as pin-ended frame objects with zero compression capacity.
- Saves a local generation manifest so generated ETABS objects can be cleared later.

SAP2000 features:

- Reads SAP2000 frame, cable, and tendon properties.
- Generates a new SAP2000 model structure or regenerates an existing generated group.
- Clears generated SAP2000 objects.
- Draws tension members as cable or tendon objects when possible.

The tab also shows calculated panel count, panel width, node count, frame count, tension-only count, group name, preview geometry, and validation messages.

### CoT Arch

The `CoT Arch` tab generates a parametric tied-arch transfer structure into an already-open ETABS model.

Geometry inputs:

- Model prefix, origin X, plane Y, base elevation, springing elevation, span, rise, upper-beam elevation, post count, arch segments per post bay, arch profile, optional custom post stations, and power-curve exponent.
- ETABS frame sections for the segmented compression arch, vertical posts, upper horizontal beam, tension tie, and support columns.
- Planar X-Z restraint option, base support condition, and per-member release presets.

ETABS features:

- Reads frame sections from the selected running ETABS instance.
- Validates section names before writing and does not silently fall back to `Default`.
- Stops if the selected ETABS model is locked; unlock the model in ETABS before generation.
- Creates shared springing joints for the arch, tie, end posts, and support columns.
- Creates the upper beam only between the two end-post top joints, with no overhang.
- Saves a local CoT Arch manifest so regenerate and clear operations target generated objects only.

The tab also shows calculated arch segment count, post count, beam segment count, node count, frame count, group name, preview geometry, and validation messages.

### ETABS Model Setup

This top-level tab currently groups ETABS setup/editing workflows.

#### Frame Assignments

The `Frame Assignments` sub-tab edits frame objects that already exist in ETABS.

- Import selected ETABS frames.
- Import frames from a named ETABS group.
- View frame name, label, story, group, current section, new section, point I/J, and length.
- Preview selected frames in 2D or 3D.
- Apply a bulk section to checked rows.
- Check or uncheck all rows.
- Update checked frame section assignments in ETABS.
- Assign selected ETABS frames to an existing or new ETABS group.
- Apply a bulk section update to a whole group.
- Update checked frame distributed loads by pattern, line load, or area load with panel width.
- Optionally replace existing loads in the selected pattern on checked frames.

### Loads

The `Loads` tab manages ETABS load patterns, static load cases, and response combinations.

Load pattern tools:

- Read ETABS load patterns.
- Create new patterns.
- Duplicate patterns.
- Edit type and self-weight multiplier.
- Apply selected pattern to ETABS.
- Mark rows for deletion and delete marked rows.
- Show usage and row status.

Static load case tools:

- Read ETABS static load cases.
- Create or duplicate cases.
- Add, remove, and clear load items.
- Edit load pattern and scale factor rows.
- Apply selected case to ETABS.
- Mark cases for deletion and delete marked rows.

Response combination tools:

- Read ETABS response combinations.
- Create or duplicate combinations.
- Add load case or combination items.
- Edit source type, source name, and scale factor.
- Apply selected combination to ETABS.
- Mark combinations for deletion and delete marked rows.
- Maintain a load-combination factor matrix for review/editing.

### Properties

The `Properties` tab manages ETABS materials, frame sections, slab/wall properties, steel imports, and tapered sections.

Top-level quick editors:

- Add/update material.
- Load database steel sections.
- Import a steel section.
- Add/update frame section.
- Add/update slab/wall property.

Material tools:

- List existing ETABS materials.
- Create, edit, apply, and delete materials.
- Edit material type, elastic modulus, Poisson ratio, unit weight, thermal expansion, concrete strength, steel yield strength, and steel ultimate strength.

Steel import tools:

- Load steel sections from `PropertyLibraries/BSShapes2006.xml`.
- Filter by shape.
- Select ETABS material.
- Import one selected section or multiple checked sections.
- Set ETABS property names before import.

Frame section tools:

- List existing ETABS frame properties.
- Create, edit, apply, and delete frame sections.
- Edit shape, material, role, depth/diameter, width, flange/wall thickness, and web/wall thickness.

Slab/wall property tools:

- List existing ETABS area properties.
- Create, edit, apply, and delete slab/wall properties.
- Edit area type, slab type, shell type, material, and thickness.

Tapered steel tools:

- Import or load a base steel section.
- Read selected ETABS members and their base section geometry.
- Choose tip end, tip depth, taper type, segment/station count, reference line, and length mode.
- Preview generated station sections.
- Create ETABS nonprismatic frame properties.
- Assign generated tapered sections to selected ETABS members.
- Review the 3D tapered preview and generated station table.

### Dome Structure

The `Dome Structure` tab generates dome geometry with shell and frame options.

- Spherical-cap dome type.
- Triangular or quadrilateral shell mesh.
- Geometry by rise plus cut heights, or partial height plus top radius.
- Ring spacing by equal height, equal radius, or equal height with refined top.
- Base center, base elevation, base radius, dome rise, lower/upper cut heights, partial height, crown radius, ring count, segment count, and angle range.
- Full 360-degree or partial dome sectors.
- Optional shell panels, ring frames, radial frames, diagonal frames, base ring, crown ring, and base supports.
- Separate shell property and frame-section assignments.
- Validate and draw/update dome objects in ETABS.
- 3D preview and warning table.

### Plate Girder

The `Plate Girder` tab generates a shell-model plate girder and runs station-based checks.

Geometry and ETABS generation:

- Origin, length, depth, flange width, web thickness, flange thickness, stiffener thickness, material strengths, and mesh divisions.
- Optional top and bottom flange shell areas.
- One or more web openings.
- Opening strengthening on top, bottom, left, and right sides.
- Separate shell properties for web, flange, and stiffeners.
- Optional top-flange area load.
- Validate and send shell objects/loads to ETABS.

Analysis:

- Variable-section Timoshenko beam stiffness analysis.
- Station results for inertia, neutral axis, moment capacity, moment demand, shear capacity, shear demand, utilization, deflection, section class, flange class, and web class.
- EN 1993-1-1 style section classification.
- Class 1/2 plastic moment resistance, Class 3 elastic moment resistance, and Class 4 warning/flag behavior.
- Plastic web shear capacity check with shear buckling warning where relevant.
- Opening effects by removing web area at stations inside each opening.
- Stiffener contribution at stations crossing stiffener strips.

### Wall / Drain

The `Wall / Drain` tab generates retaining wall and drain geometry as frame or shell models.

Supported shapes:

- One-sided wall.
- L-wall.
- U-drain.
- Box drain.

Features:

- Origin, length, height, clear width, toe length, heel length, length mesh size, and height divisions.
- Frame or shell modelling mode.
- Optional base slab.
- Optional buttress or counterfort.
- Separate frame sections or shell properties for wall, slab, and buttress/counterfort components.
- Uniform pressure load.
- Triangular pressure load.
- Normal inward, global X positive, or global X negative load direction.
- Erase/redraw or add-as-new ETABS export with offsets.
- Validation, 3D preview, section preview, and warnings.

### Railing

The `Railing` tab generates steel railing frame models.

- Railing ID, span count, post spacing, height, base elevation, start X, and start Y.
- Posts, top rail, optional mid rails, and optional bottom rail.
- Mid-rail count and bottom-rail elevation.
- Fixed or pinned base restraints.
- Separate section assignments for post, top rail, mid rail, and bottom rail.
- Optional line or point load.
- Load target group and direction in global X or global Y.
- Draw/update into ETABS with existing-group update option.
- 2D preview and warning table.

### Model Compare

The `Model Compare` tab compares ETABS model revisions.

Snapshot extraction:

- Reads live ETABS model data into a versioned JSON-compatible snapshot.
- Captures metadata, frames, areas, joints, frame properties, area properties, and materials.
- Captures frame end releases and joint restraints.
- Uses kN-m-C units for extraction and verifies the ETABS unit state.
- Can save/load snapshot JSON files through the UI.

Persistent member IDs:

- `Assign Member IDs` stamps frames and areas that do not already have a GUID.
- Tool-owned IDs use the `MCT-` prefix.
- Existing GUIDs from ETABS/Revit/IFC are preserved.
- Save the ETABS model after stamping IDs so future comparisons remain stable.

Comparison behavior:

- Compares live ETABS models directly or compares saved snapshots.
- Reports added, removed, moved, modified, and unchanged objects.
- Matches by persistent ID first, then exact/reversed geometry, then near-geometry where safe.
- Compares frame section, material, length, movement, orientation, and end releases.
- Compares area geometry, property, material, thickness, opening flag, and split/merge-style repartition cases.
- Compares frame properties, area properties, materials, joints, and restraints.
- Uses safety limits to avoid excessive moved-frame candidate checks.

Review UI:

- Project explorer summary by category.
- Added/removed/modified/unchanged totals.
- Filters by change type, object type, confidence, member type, story, review state, and search text.
- Review status options: unreviewed, reviewed, ignored, and needs checking.
- Master-detail view for selected changed objects.
- Selection command to select highlighted frames/areas/joints back in ETABS.

### IFC Structural Import

The `IFC Structural Import` tab creates a reviewable analytical model from IFC and can export recognised structural objects to ETABS.

Import inputs:

- Browse for an `.ifc` file.
- Include beams.
- Include columns.
- Optionally include slabs.
- Optionally include walls.
- Reset coordinate origin near the ETABS origin.
- Connect/condition frames.
- Recover mesh-only members.
- Use structural-wall filtering to exclude likely partitions.
- Set node snap tolerance in mm.
- Run or cancel long imports with progress reporting.

Recognition behavior:

- Uses XBIM to read IFC data.
- Supports `IfcBeam` and `IfcColumn` as analytical frames.
- Supports `IfcSlab`, `IfcWall`, and `IfcStructuralSurfaceMember` as analytical areas when enabled.
- Recognises frames from Axis representation or SweptSolid representation.
- Recovers some mesh-only prismatic members when enabled and marks them with lower confidence.
- Recognises slabs/walls as planar area boundaries with thickness where possible.
- Reads storeys, materials, section profile data, coordinates, and source GUIDs.
- Applies optional coordinate-origin reset.
- Snaps close frame endpoints.
- Reports duplicate frames, short members, and disconnected beam endpoints.
- Derives story levels from final geometry and snaps floor elements to work planes when conditioning is enabled.

Review/export outputs:

- Shows imported count, skipped count, warning count, and geometry summary.
- Displays warnings and skipped elements, with the UI capped at the first 1000 rows for performance.
- Exports full JSON results.
- Exports a Markdown review report.
- Opens the exported JSON or report.

ETABS export:

- Refreshes ETABS instances.
- Exports frames, slabs, and/or walls.
- Optionally exports medium-confidence objects with warnings.
- Optionally creates default sections for unknown sections.
- Optionally creates default materials for unknown materials.
- Preserves IFC source GUIDs through ETABS groups where supported.
- Creates required ETABS materials, frame sections, and area properties.
- Exports areas separately from frames and assigns diaphragms after the final export pass.

See `Features/IfcImport/IFC_STRUCTURAL_IMPORT_LOGIC.md` for the detailed import logic, recognition order, skip reasons, and ETABS export boundaries.

## Repository Layout

```text
CSI Modelling Tools.csproj                 Main WPF project, package references, CSI API references
CSI Modelling Tools.slnx                   Solution including app and regression harnesses
App.xaml, MainWindow.xaml                  Application startup and main tab UI
MainWindow.xaml.cs                         Main window event handlers
Controls/                                  2D/3D preview controls
Features/IfcImport/                        IFC parser, recognisers, cleanup checks, exporters, view model
Models/                                    Shared DTOs, ETABS/SAP2000 models, compare snapshots, import models
PropertyLibraries/BSShapes2006.xml         Steel section database used by import workflows
Services/                                  CSI API services, generators, validators, compare/import support
Tests/ModelCompareRegression/              Console regression tests for model comparison
Tests/ParametricTrussRegression/           Console regression tests for truss generation
ViewModels/                                MVVM view models and commands
Views/ModelCompareView.xaml                Dedicated Model Compare user control
lib/                                       Local CSI API assemblies and support files
```

## Tests And Verification

Build the full solution:

```powershell
dotnet build "CSI Modelling Tools.slnx"
```

Run Model Compare regression tests:

```powershell
dotnet run --project "Tests\ModelCompareRegression\ModelCompareRegression.csproj" --no-build
```

Expected success text currently reports `54/54 Model Compare regression tests passed.`

Run Parametric Truss regression tests:

```powershell
dotnet run --project "Tests\ParametricTrussRegression\ParametricTrussRegression.csproj" --no-build
```

Expected success text currently reports `3/3 Parametric Truss regression tests passed.`

## Important Notes

- ETABS and SAP2000 commands modify the selected running CSI model. Confirm the selected instance before applying changes.
- Many write commands unlock the CSI model before drawing or editing. Save important models before bulk operations.
- ETABS operations generally use kN-m-C units and attempt to restore the original unit state where appropriate.
- Generated object names and group names are sanitized for CSI compatibility.
- Validation and warning tables are part of the workflow; resolve critical messages before exporting or applying changes.
- IFC import is intentionally conservative. It creates a reviewable analytical model and does not attempt to solve every possible IFC geometry case.
- Model Compare is most reliable when persistent member IDs have been assigned and saved before major model revisions.
- Tapered steel generation creates analysis-model nonprismatic properties; final fabricated member design still requires engineering review.
- Plate girder analysis and generated modelling helpers are engineering aids based on the implemented assumptions in the source.

## Development Notes

- The app follows an MVVM-style structure using `ObservableObject` and `RelayCommand`.
- ETABS API workflows are concentrated in `EtabsParametricModellingService` and partial service files.
- SAP2000 support is concentrated in `Sap2000ModellingService`.
- Parametric generators and validators are separated into service classes.
- IFC import is isolated under `Features/IfcImport`.
- Model comparison snapshot, comparison, and ETABS selection models are separated under `Models/ModelCompare` and related services.
- Tests are excluded from the main WPF compile through the project file and included as separate solution projects.
- `packages.lock.json` is checked in for reproducible package restore.
