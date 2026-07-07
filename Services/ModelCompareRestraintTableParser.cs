using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

/// <summary>
/// Turns an ETABS "joint restraint assignments" display table (a flat row-major string array plus the list of
/// included field keys) into restrained-joint snapshots. This is deliberately pure and ETABS-free so the
/// parsing — the part most likely to differ across ETABS versions — can be regression-tested without COM.
///
/// It returns false when the table does not contain a recognizable point-name column and all six restraint DOF
/// columns, so the caller can fall back to the per-point restraint scan rather than trust a half-understood
/// table. That fallback is what guarantees the bulk read can never silently produce wrong restraint data.
/// </summary>
public static class ModelCompareRestraintTableParser
{
    public static bool TryParse(
        IReadOnlyList<string> fieldKeys,
        IReadOnlyList<string> tableData,
        int numberRecords,
        IReadOnlyDictionary<string, (double X, double Y, double Z)> pointCoordinates,
        out List<ModelCompareJointSnapshot> joints)
    {
        joints = [];
        if (fieldKeys == null || tableData == null || fieldKeys.Count == 0)
            return false;

        int fieldCount = fieldKeys.Count;
        int nameColumn = FindColumn(fieldKeys, "UNIQUENAME", "POINT", "JOINT", "POINTNAME", "NAME", "UNIQUEPOINTNAME");
        int uxColumn = FindColumn(fieldKeys, "U1", "UX");
        int uyColumn = FindColumn(fieldKeys, "U2", "UY");
        int uzColumn = FindColumn(fieldKeys, "U3", "UZ");
        int rxColumn = FindColumn(fieldKeys, "R1", "RX");
        int ryColumn = FindColumn(fieldKeys, "R2", "RY");
        int rzColumn = FindColumn(fieldKeys, "R3", "RZ");
        if (nameColumn < 0 || uxColumn < 0 || uyColumn < 0 || uzColumn < 0 || rxColumn < 0 || ryColumn < 0 || rzColumn < 0)
            return false;

        int recordCount = Math.Max(0, numberRecords);
        if ((long)recordCount * fieldCount > tableData.Count)
            return false;

        IReadOnlyDictionary<string, (double X, double Y, double Z)> coordinates =
            pointCoordinates ?? new Dictionary<string, (double X, double Y, double Z)>();

        var collected = new List<ModelCompareJointSnapshot>();
        for (int record = 0; record < recordCount; record++)
        {
            int rowStart = record * fieldCount;
            string name = (tableData[rowStart + nameColumn] ?? "").Trim();
            if (name.Length == 0)
                continue;

            bool ux = ParseFlag(tableData[rowStart + uxColumn]);
            bool uy = ParseFlag(tableData[rowStart + uyColumn]);
            bool uz = ParseFlag(tableData[rowStart + uzColumn]);
            bool rx = ParseFlag(tableData[rowStart + rxColumn]);
            bool ry = ParseFlag(tableData[rowStart + ryColumn]);
            bool rz = ParseFlag(tableData[rowStart + rzColumn]);

            // The restraint table should only list restrained points; skip any all-free row defensively so the
            // output matches the per-point scan (which captures restrained joints only).
            if (!(ux || uy || uz || rx || ry || rz))
                continue;

            (double X, double Y, double Z) coordinate = coordinates.TryGetValue(name, out (double X, double Y, double Z) cached)
                ? cached
                : (0d, 0d, 0d);

            collected.Add(new ModelCompareJointSnapshot
            {
                PointName = name,
                X = coordinate.X,
                Y = coordinate.Y,
                Z = coordinate.Z,
                RestraintUX = ux,
                RestraintUY = uy,
                RestraintUZ = uz,
                RestraintRX = rx,
                RestraintRY = ry,
                RestraintRZ = rz
            });
        }

        joints = collected
            .OrderBy(joint => joint.PointName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return true;
    }

    private static bool ParseFlag(string? value)
    {
        string trimmed = (value ?? "").Trim();
        return trimmed.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("True", StringComparison.OrdinalIgnoreCase) ||
            trimmed == "1";
    }

    private static int FindColumn(IReadOnlyList<string> fieldKeys, params string[] normalizedAliases)
    {
        for (int index = 0; index < fieldKeys.Count; index++)
        {
            if (normalizedAliases.Contains(Normalize(fieldKeys[index])))
                return index;
        }

        return -1;
    }

    private static string Normalize(string? value)
    {
        return new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }
}
