# IFC Structural Import Logic

This document explains how the IFC Structural Frame Importer decides what to import, how it recognises each supported element type, and why an element may be skipped.

The importer is intentionally conservative. It prefers explicit IFC analytical/parametric data, warns when geometry is inferred, and skips elements when the shape is too ambiguous.

## Overall Flow

```text
IFC file
  -> IfcImportService
      -> read project units
      -> read building storeys
      -> collect enabled element candidates
      -> recognise frames
      -> recognise areas
      -> optional coordinate origin reset
      -> node snap review
      -> duplicate review
      -> short member review
      -> connectivity review
  -> IfcImportResult
      -> JSON export
      -> review report
      -> optional ETABS frame/area export
```

The importer currently supports:

- `IfcBeam` as `AnalyticalFrameElement`
- `IfcColumn` as `AnalyticalFrameElement`
- `IfcSlab` as `AnalyticalAreaElement`, only when enabled
- `IfcWall` as `AnalyticalAreaElement`, only when enabled
- `IfcStructuralSurfaceMember` as `AnalyticalAreaElement`, only when enabled

## Options

Default frame import options are conservative:

| Option | Default | Purpose |
| --- | ---: | --- |
| `IncludeBeams` | `true` | Include `IfcBeam` candidates. |
| `IncludeColumns` | `true` | Include `IfcColumn` candidates. |
| `IncludeSlabs` | `false` | Include `IfcSlab` area candidates. |
| `IncludeWalls` | `false` | Include `IfcWall` area candidates. |
| `IncludeStructuralSurfaceMembers` | `false` | Include `IfcStructuralSurfaceMember` area candidates. |
| `EnableAdvancedGeometryRecognition` | `false` | Allow fallback Brep/mesh inference for frames. |
| `NodeSnapTolerance` | `0.020 m` | Snap nearby frame endpoints for review/export. |
| `ShortMemberMinimumLength` | `0.300 m` | Warn on very short members. |
| `ConnectivityTolerance` | `0.020 m` | Warn on isolated beam endpoints. |
| `CoordinateOriginReset` | preserve | Optionally shift imported coordinates near local origin. |

## Frame Recognition

Frame recognition applies to `IfcBeam` and `IfcColumn`.

Recognition order:

```text
1. Axis representation
2. SweptSolid representation
3. Advanced Brep/mesh inference, only if enabled
4. Skip
```

Axis and SweptSolid recognition are never replaced by the advanced fallback. The fallback runs only if both fail.

### IfcBeam

`IfcBeam` is imported as an `AnalyticalFrameElement`.

The importer first looks for an IFC `Axis` representation. If found, it tries to create a line from:

- `IfcPolyline`
- `IfcIndexedPolyCurve`
- `IfcCompositeCurve`
- `IfcTrimmedCurve`
- `IfcLine`
- mapped versions through `IfcMappedItem`

If a straight axis is found:

- start point = first axis point
- end point = last axis point
- recognition method = `Axis`
- confidence = `High`
- section may remain unknown unless SweptSolid/profile data is also available later

Curved axes are not supported. If an axis contains arc segments, the element is skipped or falls through to another recognition method.

If no usable axis is found, the importer tries `SweptSolid`.

For `SweptSolid`:

- start point = swept solid origin
- end point = origin + extrusion direction x extrusion depth
- section profile is read from the swept area where possible
- mapped swept solids are supported through `IfcMappedItem`

Supported section profiles:

- rectangle
- circle
- I-section

Unsupported profiles are imported with an unknown section only if the geometry line itself is usable.

If Axis and SweptSolid fail, and `EnableAdvancedGeometryRecognition = true`, the importer may try Brep/mesh inference. Inferred beams are always `Low` or `Medium` confidence and receive a warning requiring manual verification.

### IfcColumn

`IfcColumn` uses the same frame recognition sequence as `IfcBeam`.

The main difference is naming and expected orientation:

- rectangle sections default to a `C...` section name instead of `B...`
- connectivity checking currently focuses on beam endpoints, not column endpoints
- columns still participate as possible connected endpoints for beams

Column recognition order:

```text
Axis
  -> SweptSolid
      -> optional Brep/mesh inference
          -> skip
```

## Advanced Frame Geometry Fallback

This is controlled by:

```text
EnableAdvancedGeometryRecognition = false
```

It is off by default.

When enabled, this fallback tries to infer a frame line from physical geometry vertices if no Axis or SweptSolid is usable.

Supported source geometry:

- faceted Brep vertices
- connected face set vertices
- tessellated face set vertices

Inference idea:

```text
collect vertices
  -> estimate principal axis
  -> find two end regions
  -> centroid of each end region
  -> line between centroids
  -> estimate rectangular section from perpendicular extents
```

Safety checks:

- not enough vertices -> skip
- too many vertices -> skip as complex
- ambiguous principal direction -> skip
- inferred length too short -> skip
- member aspect ratio too low -> skip
- section estimate missing -> skip
- section aspect ratio extreme -> skip

Inferred frames are marked:

```text
RecognitionMethod = Inferred
Confidence = Low or Medium
```

They always receive this warning:

```text
Element imported using inferred geometry. Please verify centreline and section.
```

The fallback does not support:

- curved beams
- tapered members
- haunches
- complex steel connections
- major cut-outs
- very irregular shapes

## Area Recognition

Area recognition is separate from frame recognition.

It is handled by `AreaRecognitionService` and produces `AnalyticalAreaElement`.

Supported area source elements:

- `IfcSlab`
- `IfcWall`
- `IfcStructuralSurfaceMember`

All are optional and disabled by default.

### IfcSlab

`IfcSlab` is imported as an `AnalyticalAreaElement` when `IncludeSlabs = true`.

The importer looks for a usable swept/profile body:

```text
IfcSlab
  -> Body / SweptSolid
  -> rectangle or closed polyline profile
  -> mid-surface boundary
  -> thickness estimate
```

Boundary recognition:

- rectangle profile is supported
- arbitrary closed polyline profile is supported
- complex curves are skipped
- non-planar boundaries are skipped
- very complex boundaries are skipped

Thickness logic:

1. material layer thickness, if available
2. structural surface member thickness, if applicable
3. extrusion depth as fallback
4. unknown thickness warning if unresolved

Openings are not cut into the analytical area in this phase. If openings are detected, the importer warns:

```text
Openings are present and were ignored in the analytical area boundary.
```

### IfcWall

`IfcWall` is imported as an `AnalyticalAreaElement` when `IncludeWalls = true`.

The wall logic is similar to slab logic:

```text
IfcWall
  -> Body / SweptSolid
  -> profile boundary
  -> mid-surface area
  -> thickness estimate
```

The importer attempts to create a vertical analytical area from the swept body/profile.

Skipped cases:

- non-planar surface
- complex profile curve
- too many boundary points
- missing usable swept body
- complex openings

Openings are currently reported, not modelled.

### IfcStructuralSurfaceMember

`IfcStructuralSurfaceMember` is imported as an `AnalyticalAreaElement` when `IncludeStructuralSurfaceMembers = true`.

This type may already represent an analytical or structural surface, so it is useful when the IFC authoring/export pipeline includes structural analysis objects.

Recognition is still conservative:

- swept/profile body required
- planar boundary required
- thickness read from `Thickness` where available
- skipped if boundary or thickness is too ambiguous

## Storey Assignment

Storeys are read from `IfcBuildingStorey`.

For each frame or area:

1. Try direct spatial containment.
2. Try `ContainedInStructure`.
3. Infer by Z elevation using `StoreyElevationTolerance`.
4. Warn if no storey can be resolved.

## Materials

Material names are read from:

- direct `IfcMaterial`
- material profile set
- material layer set
- material list

If no material is found:

```text
MaterialName = UNKNOWN_MATERIAL
```

A warning is added.

## Cleanup And Validation

Cleanup and validation do not delete imported elements.

They only add warnings or visible cleanup actions.

### Coordinate Origin Reset

If enabled, coordinates are shifted by the first imported frame start point or first area boundary point.

The applied offset is stored in:

```text
IfcImportResult.CoordinateOffset
```

It is also reported in cleanup actions and the review report.

### Node Snapping

Frame endpoints within `NodeSnapTolerance` are snapped to a shared endpoint.

This is intended to clean tiny coordinate differences.

The implementation uses a spatial index, so it checks nearby endpoints only.

### Duplicate Detection

Possible duplicates are detected when:

- start/end points match within tolerance, or match in reverse order
- sections are similar

Duplicates are not deleted.

The implementation uses a spatial index to avoid comparing every frame with every other frame.

### Short Member Detection

Any frame shorter than `ShortMemberMinimumLength` gets a warning.

Default:

```text
300 mm
```

### Connectivity Checking

Beam endpoints are checked against nearby frame endpoints.

If no nearby endpoint is found, the beam gets a warning.

This does not modify geometry.

## ETABS Export Logic

ETABS export does not read IFC directly.

Frame flow:

```text
IfcImportResult.Frames
  -> EtabsFrameExporter
  -> ETABS frame objects
```

Area flow:

```text
IfcImportResult.Areas
  -> AreaExportToEtabs
  -> ETABS area/shell objects
```

Frame exporter and area exporter are separate.

## Common Skip Reasons

| Skip reason | Meaning |
| --- | --- |
| No usable Axis or SweptSolid representation found | The element has no supported analytical axis or swept body. |
| Axis contains arc segments | Curved members are intentionally unsupported. |
| SweptSolid extrusion direction or depth is not usable | The body does not provide a clean extrusion line. |
| Unsupported IFC profile | Section/profile type is outside rectangle/circle/I-section support. |
| Non-planar surface | Area boundary is not planar enough for a shell element. |
| Complex boundary | Area profile has too many or unsupported boundary points. |
| Openings ignored | Openings exist but are not cut into the analytical model yet. |
| Ambiguous principal direction | Advanced inferred geometry could not find a reliable member axis. |

## Performance Notes

Large IFC files can contain tens of thousands of elements and warnings.

The importer avoids full pairwise endpoint checks by using spatial indexing for:

- node snapping
- duplicate detection
- connectivity checking

The UI also limits displayed warnings/skipped rows to the first 1000 rows. JSON and markdown report exports still contain the full result.

## Important Design Boundaries

Current importer does not attempt to be a complete IFC geometry engine.

It intentionally avoids:

- replacing Axis or SweptSolid with inferred geometry
- silently trusting Brep/mesh inference
- curved beam recognition
- tapered/haunched/irregular member recognition
- wall/slab openings
- full mesh shell extraction
- automatic deletion of duplicates or short members

The main goal is to create a reviewable intermediate analytical model, not to silently produce a final ETABS model.
