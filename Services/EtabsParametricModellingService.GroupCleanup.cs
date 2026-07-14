namespace CSIModellingTools.Services;

public sealed partial class EtabsParametricModellingService
{
    private static void TryDeleteFramesInGroup(ETABSv1.cSapModel sapModel, string groupName, List<string> warnings)
    {
        int numberItems = 0;
        int[] objectTypes = [];
        string[] objectNames = [];

        try
        {
            int ret = sapModel.GroupDef.GetAssignments(groupName, ref numberItems, ref objectTypes, ref objectNames);
            if (ret != 0)
                return;

            int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));
            var frames = new List<string>();
            var points = new List<string>();
            var areas = new List<string>();

            for (int index = 0; index < count; index++)
            {
                if (objectTypes[index] == EtabsSelectedFrameObjectType)
                    frames.Add(objectNames[index]);
                else if (objectTypes[index] == EtabsSelectedPointObjectType)
                    points.Add(objectNames[index]);
                else if (objectTypes[index] == EtabsSelectedAreaObjectType)
                    areas.Add(objectNames[index]);
            }

            foreach (string frame in frames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.FrameObj.Delete(frame, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"Existing generated frame '{frame}' in group '{groupName}' could not be deleted. Return code: {deleteRet}.");
            }

            if (frames.Count > 0)
                warnings.Add($"Removed {frames.Count} existing frame object(s) from group '{groupName}' before drawing.");

            foreach (string area in areas.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.AreaObj.Delete(area, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"Existing generated shell area '{area}' in group '{groupName}' could not be deleted. Return code: {deleteRet}.");
            }

            if (areas.Count > 0)
                warnings.Add($"Removed {areas.Count} existing shell area object(s) from group '{groupName}' before drawing.");

            List<string> distinctPoints = points
                .Where(point => !string.IsNullOrWhiteSpace(point))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            int deletedPoints = TryDeleteGeneratedSpecialPoints(sapModel, distinctPoints, groupName, warnings);

            if (distinctPoints.Count > 0)
            {
                int retainedPointCount = Math.Max(0, distinctPoints.Count - deletedPoints);
                warnings.Add($"Deleted {deletedPoints} stale generated point object(s) and cleared loads/restraints on {retainedPointCount} retained point object(s) from group '{groupName}' before drawing.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Existing generated group '{groupName}' could not be cleaned before update: {ex.Message}");
        }
    }

    private static int TryDeleteGeneratedSpecialPoints(
        ETABSv1.cSapModel sapModel,
        IEnumerable<string> pointNames,
        string groupName,
        List<string> warnings)
    {
        int deleted = 0;
        foreach (string point in pointNames)
        {
            TrySetPointRestraint(sapModel, point, BuildFreePointRestraints(), $"existing generated point '{point}'", warnings);
            TryClearPointForceLoads(sapModel, point, warnings);

            try
            {
                int deleteRet = sapModel.PointObj.DeleteSpecialPoint(point, EtabsObjects);
                if (deleteRet == 0)
                    deleted++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Existing generated point '{point}' in group '{groupName}' was retained because point deletion failed: {ex.Message}");
            }
        }

        return deleted;
    }
}
