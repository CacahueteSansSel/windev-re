using System.Text.RegularExpressions;
using WindevReader.Internal;

namespace WindevReader;

internal static class UiReader
{
    private static readonly Regex identifierPattern = new(@"^[\p{L}_][\p{L}\p{N}_]*$", RegexOptions.CultureInvariant);

    private static readonly string[] prefixes = ["BTN_", "BT_", "SAI_", "EDT_", "LIB_", "TABLE_", "COL_", "COMBO_", "CMB_", "INT_", "SEL_", "IMG_", "ZR_", "GR_", "OPT_", "MENU_", "ONG_", "TAB_", "FEN_", "DEBUT_", "HAUT_", "BAS_", "FIN_", "CHAMP"];

    public static IReadOnlyList<WindevUiElement> Elements(IReadOnlyList<WindevDecodedBlock> buffers, string name, IReadOnlyList<WindevCodeBlock> code, bool includeDuplicateNames = false)
    {
        HashSet<string> owners = code.Select(codeBlock => codeBlock.OwnerName).ToHashSet(StringComparer.Ordinal);
        List<WindevUiElement> result = new List<WindevUiElement>();

        foreach (WindevDecodedBlock buffer in buffers)
        {
            IReadOnlyList<WindevString> strings = buffer.Strings;

            for (int index = 0; index < strings.Count; index++)
            {
                WindevString textValue = strings[index];
                string value = textValue.Value;

                if (value == name || !identifierPattern.IsMatch(value))
                {
                    continue;
                }

                bool candidate = prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) || owners.Contains(value) || value is "_Menu" or "CORPS" or "NUMNBPAGES" or "TITREDOC";

                if (!candidate)
                {
                    continue;
                }

                string kind = InferKind(value);

                result.Add(new WindevUiElement(value, kind, buffer.Offset, textValue.Offset, strings.Skip(index + 1).Take(3).ToArray()));
            }
        }

        return includeDuplicateNames ? result.AsReadOnly() : result.DistinctBy(entry => entry.Name).ToArray();
    }

    internal static bool IsIdentifier(string name) => identifierPattern.IsMatch(name);

    internal static string InferKind(string name)
    {
        (string Prefix, string Kind)[] categories =
        [
            ("OPT_", "MenuOption"),
            ("COL_", "Column"),
            ("BTN_", "Button"),
            ("BT_", "Button"),
            ("TABLE_", "Table"),
            ("SAI_", "Input"),
            ("LIB_", "Label"),
            ("IMG_", "Image"),
            ("GR_", "Group"),
            ("MENU_", "Menu"),
            ("DEBUT_", "ReportBlock"),
            ("HAUT_", "ReportBlock"),
            ("BAS_", "ReportBlock"),
            ("FIN_", "ReportBlock")
        ];

        foreach ((string Prefix, string Kind) category in categories)
        {
            if (name.StartsWith(category.Prefix))
            {
                return category.Kind;
            }
        }

        return name switch
        {
            "_Menu" => "Menu",
            "CORPS" => "ReportBlock",
            _ => "Unknown"
        };
    }

}
