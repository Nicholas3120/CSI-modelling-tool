namespace CSIModellingTools.Features.IfcImport;

/// <summary>
/// Assigns a rigid diaphragm to each story after the objects exist. This ties the joints
/// at each floor (frame and slab) together in-plane, creating the lateral load path and
/// stabilising beam ends that frame into elements outside the model — without needing the
/// slab and frame meshes to share nodes.
///
/// Only joints at the floor plane are included: a rigid diaphragm must be a single horizontal
/// level, so off-level joints on the story (wall intermediate nodes, ramp/stair landings,
/// sloped-beam ends) are excluded. Including them triggers ETABS's "rigid diaphragm connection
/// between joints at different elevations" warning and corrupts the lateral model.
/// </summary>
internal static class EtabsDiaphragmSetup
{
    // A joint counts as "on the floor" if within this vertical distance of the story elevation.
    private const double FloorPlaneTolerance = 0.150;

    public static int Configure(ETABSv1.cSapModel sapModel)
    {
        int numberStories = 0;
        string[] stories = [];
        if (sapModel.Story.GetNameList(ref numberStories, ref stories) != 0)
            return 0;

        int assignedStories = 0;
        foreach (string story in stories.Take(Math.Min(numberStories, stories.Length)))
        {
            // The base is a support level, not a floor — it gets restraints, not a diaphragm.
            if (string.IsNullOrWhiteSpace(story) || string.Equals(story, "Base", StringComparison.OrdinalIgnoreCase))
                continue;

            double storyElevation = 0;
            if (sapModel.Story.GetElevation(story, ref storyElevation) != 0)
                continue;

            int numberPoints = 0;
            string[] points = [];
            if (sapModel.PointObj.GetNameListOnStory(story, ref numberPoints, ref points) != 0)
                continue;

            string diaphragmName = EtabsFrameExporter.BuildSafeEtabsName("D_" + story, "D_", 60);
            sapModel.Diaphragm.SetDiaphragm(diaphragmName, false); // false => rigid

            bool assignedAny = false;
            foreach (string point in points.Take(Math.Min(numberPoints, points.Length)))
            {
                // A rigid diaphragm is a single horizontal plane; only tie joints at the floor
                // level, or ETABS warns about "joints at different elevations" and the rigid
                // link distorts off-level members.
                double x = 0, y = 0, z = 0;
                if (sapModel.PointObj.GetCoordCartesian(point, ref x, ref y, ref z, "Global") != 0)
                    continue;
                if (Math.Abs(z - storyElevation) > FloorPlaneTolerance)
                    continue;

                if (sapModel.PointObj.SetDiaphragm(point, ETABSv1.eDiaphragmOption.DefinedDiaphragm, diaphragmName) == 0)
                    assignedAny = true;
            }

            if (assignedAny)
                assignedStories++;
        }

        return assignedStories;
    }
}
