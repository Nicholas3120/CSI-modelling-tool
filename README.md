# CSI Modelling Tools

CSI Modelling Tools is a Windows WPF desktop application for generating, editing, checking, and sending structural modelling data to CSI ETABS. The application is written in C# for .NET 8, uses the ETABS COM API through `lib/ETABSv1.dll`, and uses HelixToolkit for several 3D previews.

The tool is intended for structural modelling workflows where repetitive ETABS objects, section properties, load definitions, or design-check tables can be prepared in a focused UI and then pushed into an open ETABS model.

## Main Capabilities

- Connects to running ETABS instances and reads model data such as frame sections, shell properties, materials, load patterns, load combinations, stories, and groups.
- Generates parametric trusses, spiral staircases, fish-belly trusses, variable-panel trusses, domes, plate girders, railings, walls, and drains.
- Sends generated frame and shell objects to ETABS, either by erasing and redrawing an existing tool-generated group or by adding a new copy with user-defined offsets.
- Imports existing ETABS frame objects for bulk section reassignment, group assignment, and distributed-load updates.
- Manages ETABS load patterns, static load cases, and response combinations.
- Manages ETABS material, frame, slab, and wall properties, including import of BS steel database sections from `PropertyLibraries/BSShapes2006.xml`.
- Creates and assigns tapered steel nonprismatic frame properties from selected ETABS members.
- Assigns hydrostatic shell loads to selected shells, shell groups, or named shell lists.
- Provides local calculators for beam splice connection checks and pile eccentricity point-load/tie-beam transfer calculations.
- Provides live 2D or 3D previews, validation messages, warnings, and summary tables before ETABS write operations.

## Requirements

- Windows.
- .NET 8 SDK or newer. The repository includes `global.json` with SDK version `8.0.100` and `rollForward` set to `latestMajor`.
- CSI ETABS installed and available through its COM API for ETABS-connected features.
- The ETABS API interop file at `lib/ETABSv1.dll`. The project file checks for this file before building.
- Internet or a populated NuGet cache for first restore, because the project depends on `HelixToolkit.Wpf`.

The application can still open and run local preview/calculation features without a connected ETABS model, but ETABS read/write features need an active ETABS API instance.

## Build And Run

From the repository root:

```powershell
dotnet restore "CSI Modelling Tools.csproj"
dotnet build "CSI Modelling Tools.csproj"
dotnet run --project "CSI Modelling Tools.csproj"
```

To run from Visual Studio, open `CSI Modelling Tools.slnx`, restore packages, build, and start the WPF project.

Build outputs are written under `bin/` and `obj/`, which are intentionally ignored by git.

## General ETABS Workflow

Most ETABS-facing tabs follow the same pattern:

1. Open the target ETABS model.
2. Start CSI Modelling Tools.
3. Click `Refresh ETABS Instances` to discover running ETABS sessions.
4. Select the target ETABS instance.
5. Click `Read ETABS Data`, `Read Properties`, or `Read Load Data`, depending on the tab.
6. Enter geometry, load, section, material, or property inputs.
7. Review the preview and warnings/messages table.
8. Click `Validate`, `Preview`, or the relevant apply command.
9. Use the ETABS write command, such as `Send to ETABS`, `Draw/Update`, `Assign to ETABS`, `Apply`, or `Update Sections in ETABS`.

For generated objects, the tool uses safe ETABS names and tool-specific group prefixes such as `WPF_TRUSS_`, `WPF_DOME_`, `WPF_PLATE_GIRDER_`, `WPF_RAILING_`, and `WPF_WALL_DRAIN_`.

## Feature Guide

### ETABS Parametric Modelling

The `ETABS Parametric Modelling` tab generates frame and optional shell models from parametric inputs.

Supported structure types:

- Warren truss.
- Pratt truss.
- Howe truss.
- K truss.
- Simple frame.
- Spiral staircase.
- Fish-belly truss.
- Variable-panel-width truss.

Core inputs include truss ID, group name, manual span or two selected ETABS insertion points, height, panel count, chord slope modes, frame section assignments, support-node rules, and support restraint type.

Specialized inputs include:

- Spiral staircase geometry: center, base elevation, total height, inner and outer radius, step count, total rotation, start angle, and clockwise or anticlockwise rotation.
- Spiral staircase member options: inner stringer, outer stringer, radial tread beams, tread shell plates, central column, top landing beam, and bottom landing beam.
- Fish-belly geometry: start point, span, panel count, end depth, middle depth, direction angle, top chord slope, bottom chord shape, and web pattern.
- Variable-panel geometry: start point, span, panel count, truss depth, end-panel width ratio, middle-panel width ratio, direction angle, width variation, and web pattern.
- Fish-belly and variable-panel section modes, including same-section-for-all or separate top chord, bottom chord, vertical web, and diagonal web sections.
- Moment release options for truss behavior.

Load features:

- Add multiple truss load definitions.
- Target top chord or bottom chord.
- Enter line load in kN/m or area load in kPa with tributary panel width.
- Apply loads as equivalent panel-node loads or ETABS member line loads.
- Store load definitions in the on-screen table and remove or clear them before export.

Export features:

- Validate generated nodes, members, loads, supports, sections, and ETABS selections.
- Send generated model to ETABS.
- Choose erase-and-redraw for the existing generated group or add-as-new with X, Y, and Z offsets.

### Existing ETABS Sections

The `Existing ETABS Sections` tab is for editing frame objects that already exist in ETABS.

Features:

- Import selected ETABS frames.
- Import all frames from a named ETABS group.
- View imported frame name, label, story, group, current section, new section, end points, and length.
- Preview the selected imported frame in 2D or 3D.
- Apply one bulk frame section to checked rows.
- Check all or uncheck all rows.
- Update checked frame section assignments in ETABS.
- Assign selected ETABS frames to an existing or new ETABS group.
- Apply a bulk section update to a whole ETABS group.
- Update checked frame distributed loads by selecting a load pattern, entering line load or area load with panel width, and optionally replacing existing loads in that pattern.

### Dome Structure

The `Dome Structure` tab generates dome geometry with shell and frame options.

Features:

- Spherical-cap dome type.
- Triangular or quadrilateral shell mesh.
- Geometry input by rise plus cut heights, or by partial height plus top radius.
- Ring spacing by equal height, equal radius, or equal height with refined top.
- Base center, base elevation, base radius, dome rise, lower and upper cut heights, partial dome height, crown ring radius, ring count, segment count, and angular range.
- Full 360-degree or partial dome sectors.
- Optional shell panels, ring frames, radial frames, diagonal frames, base ring, crown ring, and base supports.
- Separate shell property, ring section, radial section, diagonal section, base ring section, and crown ring section assignments.
- Validation and draw/update into ETABS.
- 3D preview and warning table.

### Plate Girder

The `Plate Girder` tab generates a shell-model plate girder and performs station-based section checks.

Geometry and modelling features:

- Origin, length, girder depth, flange width, web thickness, flange thickness, stiffener thickness, and mesh divisions.
- Optional top and bottom flange shell areas.
- One or more web openings with center, size, strengthening options, stiffener width, and stiffener extension.
- Separate shell property assignment for web, flange, and stiffener panels.
- Optional top-flange area load with selected load pattern.

Analysis features:

- Variable-section Timoshenko beam stiffness analysis.
- Station results for inertia, neutral axis, moment capacity, demand moment, shear capacity, demand shear, utilization, deflection, section class, flange class, and web class.
- EN 1993-1-1 style section classification.
- Moment capacity based on Class 1/2 plastic resistance, Class 3 elastic resistance, and Class 4 flagged as requiring effective-section treatment.
- Plastic web shear capacity checks with warnings where shear buckling checks are needed.
- Opening effects by removing web area at stations inside opening widths.
- Stiffener contribution at stations crossing stiffener strips.

ETABS features:

- Read ETABS shell properties and load patterns.
- Validate generated shell mesh and assignments.
- Send generated shell objects and optional load to ETABS.

### Wall / Drain

The `Wall / Drain` tab generates retaining wall and drain geometry as either frame or shell models.

Supported shapes:

- One-sided wall.
- L-wall.
- U-drain.
- Box drain.

Features:

- Origin, length, height, clear width, toe length, heel length, length mesh size, and height divisions.
- Frame or shell modelling mode.
- Optional base slab for one-sided wall mode.
- Optional buttress or counterfort for retaining wall shapes.
- Separate frame section assignments for wall, slab, and buttress members.
- Separate shell property assignments for wall, slab, and buttress panels.
- Uniform pressure load and triangular pressure load.
- Load direction options including normal inward, global X positive, and global X negative.
- Export as erase-and-redraw or add-as-new with offset.
- Validation, 3D preview, section preview, and warning table.

### Railing

The `railing` tab generates a steel railing frame model.

Features:

- Railing ID, span count, post spacing, railing height, base elevation, start X, and start Y.
- Top rail, post, optional mid rails, and optional bottom rail.
- Configurable mid-rail count and bottom rail elevation.
- Fixed or pinned base restraints.
- Separate section assignment for posts, top rail, mid rail, and bottom rail.
- Optional line load or point load applied to selected railing members or load reference nodes.
- Load direction in global X or global Y.
- Draw/update into ETABS with an option to update the existing generated railing group.
- 2D preview and warning table.

### Load Case / Combination

The `Load Case / Combination` tab edits ETABS load definitions.

Load pattern features:

- Read ETABS load patterns.
- Create new patterns.
- Duplicate patterns.
- Edit pattern type and self-weight multiplier.
- Apply selected pattern to ETABS.
- Mark patterns for deletion and delete marked rows.
- Show usage summaries.

Static load case features:

- Read ETABS static load cases.
- Create or duplicate cases.
- Add, remove, and clear load items.
- Set load pattern and scale factor rows.
- Apply selected case to ETABS.
- Mark cases for deletion and delete marked rows.

Response combination features:

- Read ETABS combinations.
- Create or duplicate combinations.
- Add load case items and combination items.
- Edit source type, source name, and scale factor.
- Apply selected combination to ETABS.
- Mark combinations for deletion and delete marked rows.
- Load selected combination into the editor.
- Maintain a load-combination factor matrix for quick factor review/editing.

### Sections / Materials

The `Sections / Materials` tab manages ETABS material and section properties.

Top-level quick editors:

- Material creation/update with type, elastic modulus, Poisson ratio, thermal expansion, unit weight, concrete strength, steel yield strength, and steel ultimate strength.
- Steel section import from database.
- Frame section creation/update.
- Slab/wall shell property creation/update.

Material tab:

- List ETABS materials.
- Create, edit, apply, and delete material properties.
- Support concrete and steel design fields.

Import Steel tab:

- Load steel sections from `PropertyLibraries/BSShapes2006.xml`.
- Filter by shape type.
- Select material.
- Import one selected section or multiple checked sections.
- Set ETABS property names before import.

Frame Sections tab:

- List existing ETABS frame properties.
- Create, edit, apply, and delete frame sections.
- Supported UI shape families include concrete rectangular and steel-style sections with depth/diameter, width, flange/wall thickness, and web/wall thickness inputs.
- Select section role such as beam/general.

Slab / Wall tab:

- List existing ETABS area properties.
- Create, edit, apply, and delete slab/wall properties.
- Set area type, slab type, shell type, material, and thickness.

Tapered Steel section tab:

- Import or load a base steel section.
- Read selected ETABS members and their base section geometry.
- Preview generated tapered station sections.
- Choose tip end, tip depth, taper type, station count, reference line, and full-member-length assignment.
- Create ETABS nonprismatic frame properties.
- Assign generated tapered section to selected ETABS members.
- Display a 3D tapered steel preview and generated station table.

## Repository Layout

```text
CSI Modelling Tools.csproj                 WPF project file and ETABS/Helix references
CSI Modelling Tools.slnx                   Visual Studio solution file
App.xaml, MainWindow.xaml                  WPF application and main tabbed UI
MainWindow.xaml.cs                         Main window event handlers
Models/                                    Data transfer objects, enums, rows, and result models
ViewModels/                                UI state, commands, validation calls, and data binding logic
Services/                                  ETABS API service, geometry generators, validators, calculators
Controls/                                  Custom 2D and 3D preview controls
PropertyLibraries/BSShapes2006.xml         Steel section database used by import workflows
lib/ETABSv1.dll                            ETABS API interop dependency copied to build output
```

## Important Notes

- ETABS-connected commands act on the selected running ETABS instance. Always confirm the target instance and model before applying updates.
- Many ETABS write commands unlock the model as needed through the ETABS API. Save important ETABS models before bulk updates.
- The tool standardizes ETABS present units to kN, m, C for many API operations and attempts to restore previous units afterward.
- Generated object names and group names are sanitized for ETABS compatibility.
- Validation tables report critical issues, warnings, and informational messages. Do not send or assign objects when critical validation messages are present.
- Plate girder and beam splice calculations are engineering aids based on the implemented formulas and assumptions in the source code. Final design checks remain the responsibility of the engineer.
- Tapered steel generation creates analysis-model nonprismatic frame properties. Final fabricated tapered/cut member design must be checked separately.

## Development Notes

- The project uses MVVM-style view models with `ObservableObject` and `RelayCommand`.
- Geometry generation is separated into service classes such as `ParametricTrussGenerator`, `ParametricDomeGenerator`, `PlateGirderGenerator`, `SteelRailingGenerator`, and `WallDrainGenerator`.
- ETABS API interactions are centralized in `EtabsParametricModellingService`.
- Validation logic is separated into validator services for generated model types.
- Custom preview controls render WPF 2D drawings or HelixToolkit 3D scenes.
- `packages.lock.json` is checked in for reproducible package restore.

