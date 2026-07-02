namespace CSIModellingTools.Features.IfcImport;

/// <summary>
/// Assigns a rigid diaphragm to every story after the objects exist. This ties all joints
/// at each floor (frame and slab) together in-plane, creating the lateral load path and
/// stabilising beam ends that frame into elements outside the model — without needing the
/// slab and frame meshes to share nodes.
/// </summary>
internal static class EtabsDiaphragmSetup
{
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

            string diaphragmName = EtabsFrameExporter.BuildSafeEtabsName("D_" + story, "D_", 60);
            sapModel.Diaphragm.SetDiaphragm(diaphragmName, false); // false => rigid

            int numberPoints = 0;
            string[] points = [];
            if (sapModel.PointObj.GetNameListOnStory(story, ref numberPoints, ref points) != 0)
                continue;

            bool assignedAny = false;
            foreach (string point in points.Take(Math.Min(numberPoints, points.Length)))
            {
                if (sapModel.PointObj.SetDiaphragm(point, ETABSv1.eDiaphragmOption.DefinedDiaphragm, diaphragmName) == 0)
                    assignedAny = true;
            }

            if (assignedAny)
                assignedStories++;
        }

        return assignedStories;
    }
}
