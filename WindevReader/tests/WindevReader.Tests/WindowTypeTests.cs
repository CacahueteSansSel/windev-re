using System.Buffers.Binary;
using WindevReader;

internal static class WindowTypeTests
{
    internal static void Run(string filename, Action<bool, string> check, List<string> checks)
    {
        WdwWindevWindow window = WdwWindevWindow.Open(filename);
        (string Name, WindevControlType Type, int Offset, int Length, int Body)[] expected =
        [
            ("SAI_SansNom1", WindevControlType.Input, 15906, 2822, 18500),
            ("LIB_SansNom1", WindevControlType.Label, 18728, 665, 19313),
            ("BTN_SansNom1", WindevControlType.Button, 19393, 1839, 21014),
            ("IMG_SansNom1", WindevControlType.Image, 21232, 1772, 21903),
            ("COMBO_SansNom1", WindevControlType.ComboBox, 23004, 6656, 27587),
            ("TABLE_PERSONNE", WindevControlType.Table, 29660, 30935, 43908),
            ("SEL_SansNom1", WindevControlType.RadioButton, 60595, 2057, 62353),
            ("INT_SansNom1", WindevControlType.CheckBox, 62652, 2119, 64417),
            ("ONG_SansNom1", WindevControlType.Tab, 64771, 4344, 67033),
            ("BTN_Le_bouton_dans_l_onglet", WindevControlType.Button, 67099, 1870, 68735)
        ];

        check(window.Header.WindevVersion == 28 && window.PropertyBuffers.Single().Data.Length == 69115, "Mixed-control window has the supplied version and decoded size");
        check(window.Elements.Count(element => element.ControlRecord is not null) == 17, "Seventeen named control records exclude internal list/style objects and the Express label");

        foreach ((string Name, WindevControlType Type, int Offset, int Length, int Body) item in expected)
        {
            WindevUiElement element = window.Elements.Single(candidate => candidate.Name == item.Name);
            WindevUiControlRecord record = element.ControlRecord!;

            check(element.ControlType == item.Type && record.RawTypeCode == (uint)item.Type, "Control family is read from its serialized numeric code");
            check(record.Offset == item.Offset && record.ByteLength == item.Length && record.SpecificPropertiesOffset == item.Body, "Control envelope lengths determine the specific payload position");
            check(record.RawDescriptor == 628 && record.CommonPropertiesOffset == record.Offset + 16, "Version 28 record header and common-property slice are explicit");
        }

        WindevUiControlRecord combo = Find(window, "COMBO_SansNom1");
        WindevUiControlRecord selector = Find(window, "SEL_SansNom1");
        WindevUiControlRecord checkBox = Find(window, "INT_SansNom1");
        WindevUiControlRecord image = Find(window, "IMG_SansNom1");
        WindevUiControlRecord tab = Find(window, "ONG_SansNom1");
        WindevUiTableBinding binding = Find(window, "TABLE_PERSONNE").TableBinding!;

        check(combo.ItemsDecoded && combo.RawItemsText?.Offset == 29421 && combo.RawItemsText.Value == "Huile\nPain\nEau\nFarine\nTortue", "Combo literal content is a single LF-separated framed string inside its internal list");
        check(combo.Items.Select(item => item.Text).SequenceEqual(new string[] { "Huile", "Pain", "Eau", "Farine", "Tortue" }), "Combo items retain their stored order");
        check(combo.Items.Select(item => item.TextOffset).SequenceEqual(new int[] { 29429, 29435, 29440, 29444, 29451 }), "Combo item offsets identify their text bytes, not invented framed strings");
        check(selector.ItemsDecoded && selector.Items.Select(item => item.Text).SequenceEqual(new string[] { "Avec la sauce", "Sans la sauce" }), "Selector contains the sauce choices actually stored in this file");
        check(checkBox.ItemsDecoded && checkBox.Items.Select(item => item.Text).SequenceEqual(new string[] { "135EUR", "200EUR", "500EUR" }), "Checkbox contains the amounts actually stored in this file");
        check(selector.Items.Select(item => item.RecordOffset).SequenceEqual(new int?[] { 62361, 62432 }), "Selector option records have framed captions and 45 metadata bytes");
        check(checkBox.Items.Select(item => item.MetadataOffset).SequenceEqual(new int?[] { 64444, 64508, 64572 }), "Checkbox option metadata positions are preserved without assigning value or selection semantics");
        check(image.ImageSource?.Offset == 21923 && image.ImageSource.Value.EndsWith("\\cul.png", StringComparison.Ordinal), "Image reference is read from the image payload without accessing that path");
        check(window.Elements.Single(element => element.Name == "LIB_SansNom1").Caption == "Libellé", "UTF-8 label caption is preserved");
        check(binding.TableName.Value == "PERSONNE" && binding.TableName.Offset == 43921 && binding.BrowseKey.Value == "IDPERSONNE", "Data table and browsing key are read from distinct structural positions");
        check(binding.DeclaredColumnCount == 7 && binding.ColumnsDecoded && binding.Columns.Count == 7, "All declared table columns satisfy the duplicated-name descriptor profile");
        check(binding.Columns.Select(column => column.DataBinding.Value).SequenceEqual(new string[] { "PERSONNE.IDPERSONNE", "PERSONNE.NOM", "PERSONNE.PRENOM", "PERSONNE.AGE1", "PERSONNE.FLAGADMIN", "PERSONNE.IDPERSONNEPASCLE", "PERSONNE.NOMPRENOMAGE1" }), "Column bindings agree with the supplied version 28 schema, including its composite key");
        check(binding.Columns.Select(column => column.Width).SequenceEqual(new uint[] { 162, 306, 306, 78, 25, 156, 99 }), "Table columns expose widths instead of fabricated control rectangles");
        check(binding.Columns[4].RawEditorTypeCode == 5 && binding.Columns[3].RawEditorTypeCode == 2, "Column editor family is distinct from the HFSQL boolean/integer type");
        check(tab.TabPagesDecoded && tab.TabPages.Count == 2 && tab.TabPages[0].Caption.Value == "Volet 1" && tab.TabPages[1].Caption.Value == "Volet 2", "Tab page captions follow the declared count");
        check(tab.TabPages[0].ChildRecordOffsets.SequenceEqual(new int[] { 67099 }) && tab.TabPages[1].ChildRecordOffsets.Count == 0, "The button is structurally contained in the first page and the second page is empty");
        check(Find(window, "BTN_Le_bouton_dans_l_onglet").Offset == tab.TabPages[0].ChildRecordOffsets[0], "Page membership resolves through record offsets rather than matching names or coordinates");

        VerifyMutations(window, check);
        VerifyProfile(window, 628, check);

        checks.Add("WDW mixed controls 28: bounded records expose numeric families 2/3/4/5/6/8/9/14/16, LF combo content, counted choice captions, image reference, PERSONNE browsing/column bindings and two tab pages with one contained button. Stored selector/checkbox captions reverse the supplied prose description. Renamed and changed-type controls follow bytes; malformed local layouts remain unknown without searching adjacent strings. Family envelopes corroborated in versions 24/25/29/31; specific content remains limited to 28.");
    }

    internal static void VerifyProfile(WdwWindevWindow window, uint descriptor, Action<bool, string> check)
    {
        WindevUiElement[] controls = window.Elements.Where(element => element.ControlRecord is not null).ToArray();

        check(controls.Length > 0, "Version profile has bounded named control records");

        foreach (WindevUiElement element in controls)
        {
            WindevUiControlRecord record = element.ControlRecord!;

            check(record.RawDescriptor == descriptor && record.Offset + record.ByteLength <= window.PropertyBuffers[0].Data.Length, "Record envelope uses the version-specific descriptor and stays within its buffer");
            check(element.Offset >= record.CommonPropertiesOffset && element.Offset < record.SpecificPropertiesOffset, "Element name belongs to its own common-property slice");

            if (element.Name.StartsWith("BTN_", StringComparison.Ordinal))
            {
                check(element.ControlType == WindevControlType.Button, "Existing button family corroborates code 4 across versions");
            }
        }
    }

    private static WindevUiControlRecord Find(WdwWindevWindow window, string name) => window.Elements.Single(element => element.Name == name).ControlRecord!;

    private static void VerifyMutations(WdwWindevWindow window, Action<bool, string> check)
    {
        string temporaryFile = Path.Combine(Path.GetTempPath(), "window-types-" + Guid.NewGuid().ToString("N") + ".wdw");

        try
        {
            byte[] changed = window.PropertyBuffers[0].Data.ToArray();

            "XXXXX_SansNom1"u8.CopyTo(changed.AsSpan(27335));

            WindevUiElement renamed = OpenChanged(window, changed, temporaryFile).Elements.Single(element => element.Name == "XXXXX_SansNom1");

            check(renamed.InferredKind == "Unknown" && renamed.ControlType == WindevControlType.ComboBox && renamed.ControlRecord?.Items.Count == 5, "A control without a recognized naming prefix still exposes its serialized type and items");

            changed = window.PropertyBuffers[0].Data.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(23008), 0xffffffff);

            WindevUiElement unknown = OpenChanged(window, changed, temporaryFile).Elements.Single(element => element.Name == "COMBO_SansNom1");

            check(unknown.ControlType is null && unknown.ControlRecord?.RawTypeCode == uint.MaxValue && unknown.ControlRecord.ItemsDecoded == false, "Unknown wire codes remain raw and are not replaced by prefix guesses");

            changed = window.PropertyBuffers[0].Data.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(60599), 5);

            check(Find(OpenChanged(window, changed, temporaryFile), "SEL_SansNom1").Type == WindevControlType.CheckBox, "Changing only the type code changes the family despite the selector name");

            foreach ((int Offset, uint Value) mutation in new (int, uint)[] { (23004, 0), (23004, uint.MaxValue), (23016, uint.MaxValue), (23012, 630) })
            {
                changed = window.PropertyBuffers[0].Data.ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(mutation.Offset), mutation.Value);

                check(OpenChanged(window, changed, temporaryFile).Elements.Single(element => element.Name == "COMBO_SansNom1").ControlRecord is null, "Invalid envelope length, descriptor or common-property extent cannot produce a typed record");
            }

            foreach ((int Offset, uint Value) mutation in new (int, uint)[] { (27587, uint.MaxValue), (27591, 8), (29421, 0), (29426, uint.MaxValue) })
            {
                changed = window.PropertyBuffers[0].Data.ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(mutation.Offset), mutation.Value);

                check(!Find(OpenChanged(window, changed, temporaryFile), "COMBO_SansNom1").ItemsDecoded, "Malformed internal list or content framing does not pick nearby strings as combo items");
            }

            changed = window.PropertyBuffers[0].Data.ToArray();
            "Livre"u8.CopyTo(changed.AsSpan(29429));

            check(Find(OpenChanged(window, changed, temporaryFile), "COMBO_SansNom1").Items[0].Text == "Livre", "Combo content is decoded dynamically rather than filled from fixture expectations");

            foreach (int offset in new int[] { 62357, 64421 })
            {
                changed = window.PropertyBuffers[0].Data.ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(offset), 10001);

                WdwWindevWindow excessive = OpenChanged(window, changed, temporaryFile);

                check(!Find(excessive, offset == 62357 ? "SEL_SansNom1" : "INT_SansNom1").ItemsDecoded, "Excessive choice counts stop optional decoding before allocation");
            }

            changed = window.PropertyBuffers[0].Data.ToArray();
            changed[62432] = 0;

            check(!Find(OpenChanged(window, changed, temporaryFile), "SEL_SansNom1").ItemsDecoded, "A damaged later option does not leave an apparently complete partial option list");

            changed = window.PropertyBuffers[0].Data.ToArray();
            changed[44174] = 0;

            check(Find(OpenChanged(window, changed, temporaryFile), "TABLE_PERSONNE").TableBinding?.ColumnsDecoded == false, "A missing column descriptor invalidates the complete column list while preserving its table binding");

            changed = window.PropertyBuffers[0].Data.ToArray();
            changed[43921] = 0;

            check(Find(OpenChanged(window, changed, temporaryFile), "TABLE_PERSONNE").TableBinding is null, "Table binding is not reconstructed from adjacent column references");

            changed = window.PropertyBuffers[0].Data.ToArray();
            changed[21923] = 0;

            check(Find(OpenChanged(window, changed, temporaryFile), "IMG_SansNom1").ImageSource is null, "Damaged image payload is not replaced by the earlier common image string");

            foreach ((int Offset, uint Value) mutation in new (int, uint)[] { (67049, 10001), (67095, 10001), (67099, 0), (67099, uint.MaxValue), (68981, 604) })
            {
                changed = window.PropertyBuffers[0].Data.ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(mutation.Offset), mutation.Value);

                check(!Find(OpenChanged(window, changed, temporaryFile), "ONG_SansNom1").TabPagesDecoded, "Invalid page counts, child extents and page descriptors do not fabricate hierarchy");
            }
        }
        finally
        {
            File.Delete(temporaryFile);
        }
    }

    private static WdwWindevWindow OpenChanged(WdwWindevWindow window, byte[] properties, string filename)
    {
        File.WriteAllBytes(filename, WindowControlTests.ReplaceProperties(window, properties));

        return WdwWindevWindow.Open(filename);
    }
}
