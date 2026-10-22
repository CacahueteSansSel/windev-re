using System.Buffers.Binary;
using System.Text;
using WindevReader;

internal static class Windev28WindowTests
{
    internal static void Run(string filename, string originalRoot, Action<bool, string> check, List<string> checks)
    {
        WdwWindevWindow window = WdwWindevWindow.Open(filename);
        (string Name, int X, int Y, bool Visible, WindevControlState State, bool TabStop, string Caption)[] expected =
        [
            ("BTN_Bouton_isible", 6, 6, true, WindevControlState.Active, true, "Bouton visible"),
            ("BTN_Bouton_invisible", 6, 36, false, WindevControlState.Active, true, "Bouton invisible"),
            ("BTN_Bouton_actif", 106, 6, true, WindevControlState.Active, true, "Bouton actif"),
            ("BTN_Bouton_inactif", 106, 36, true, WindevControlState.Inactive, true, "Bouton inactif"),
            ("BTN_Bouton_grisé", 106, 66, true, WindevControlState.GrayedOut, true, "Bouton grisé"),
            ("BTN_Bouton_tab", 206, 6, true, WindevControlState.Active, true, "Bouton accessible par tab"),
            ("BTN_Bouton_sanstab", 206, 36, true, WindevControlState.Active, false, "Bouton pas accessible par tab")
        ];

        check(window.Header.WindevVersion == 28 && window.Header.PayloadOffset == 241, "Supplied window 28 envelope");
        check(window.Bounds == new WindevBounds(0, 0, 800, 600) && window.Caption == "TestChamps", "Window 28 root uses descriptor 607 and caption displacement 563");
        check(window.PropertyBuffers.Single().Data.Length == 15905, "Window 28 decoded property size");
        check(window.Elements.Count(element => element.InferredKind == "Button") == 7, "Seven distinct buttons retain their exact stored names");

        foreach ((string Name, int X, int Y, bool Visible, WindevControlState State, bool TabStop, string Caption) item in expected)
        {
            WindevUiElement element = window.Elements.Single(candidate => candidate.Name == item.Name);
            int nameEnd = element.Layout!.IdentifierOffset;
            int width = item.Name.Contains("tab", StringComparison.Ordinal) ? 156 : 94;

            check(element.Bounds == new WindevBounds(item.X, item.Y, width, 24), "Button rectangle read from the version 28 descriptor");
            check(element.Caption == item.Caption, "Version 28 deferred button caption at common-tail end plus 194");
            check(element.IsVisible == item.Visible, "Initial visibility agrees with the supplied differential specimen");
            check(element.State == item.State, "Active, inactive and grayed states remain distinct");
            check(element.IsTabStop == item.TabStop, "Tab access agrees with the supplied differential specimen");
            check(element.Interaction?.VisibilityOffset == nameEnd + 40 && element.Interaction.StateOffset == nameEnd + 47, "Simple-header interaction offsets use decoded-buffer positions");
            check(element.Interaction?.TabAccessOffset == nameEnd + 85, "Tab-access position accounts for both empty text slots");
        }

        WindevUiElement inactive = window.Elements.Single(element => element.Name == "BTN_Bouton_inactif");
        WindevUiElement hidden = window.Elements.Single(element => element.Name == "BTN_Bouton_invisible");

        check(inactive.IsVisible == true && inactive.IsTabStop == true, "Inactive state does not overwrite independent stored visibility or Tab settings");
        check(hidden.State == WindevControlState.Active && hidden.IsTabStop == true, "Hidden control retains its separate active and Tab settings");

        VerifyChangedProperties(window, check);
        VerifyVersion31(originalRoot, check);

        checks.Add("WDW 28: seven differential buttons isolate visibility, active/inactive/grayed state and Tab access; exact names, root, rectangles and captions verified. Unknown 32-bit codes remain null with raw values preserved. Optional header/value strings relocate state and Tab offsets. Version 29 settings and version 31 context-menu/tooltip/image-value layouts corroborate the reader. Opacity, input type and unprovided version 30 remain unvalidated.");
    }

    private static void VerifyChangedProperties(WdwWindevWindow window, Action<bool, string> check)
    {
        string temporaryFile = Path.Combine(Path.GetTempPath(), "window-states-" + Guid.NewGuid().ToString("N") + ".wdw");

        try
        {
            byte[] changed = window.PropertyBuffers[0].Data.ToArray();

            BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(3965), 256);
            BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(9732), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(13604), 2);
            WriteProperties(window, changed, temporaryFile);

            WdwWindevWindow unknown = WdwWindevWindow.Open(temporaryFile);
            WindevUiElement visibility = unknown.Elements.Single(element => element.Name == "BTN_Bouton_isible");
            WindevUiElement state = unknown.Elements.Single(element => element.Name == "BTN_Bouton_inactif");
            WindevUiElement tab = unknown.Elements.Single(element => element.Name == "BTN_Bouton_tab");

            check(visibility.IsVisible is null && visibility.Interaction?.RawVisibility == 256, "Unknown visibility word is not truncated or converted to a boolean");
            check(state.State is null && state.Interaction?.RawStateCode == 2, "Unobserved state code remains raw instead of being assigned a guessed meaning");
            check(tab.IsTabStop is null && tab.Interaction?.RawTabAccess == 2, "Unknown Tab word does not become true merely because it is nonzero");

            changed = window.PropertyBuffers[0].Data.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(7814), 1);
            WriteProperties(window, changed, temporaryFile);

            WindevUiElement renamedState = WdwWindevWindow.Open(temporaryFile).Elements.Single(element => element.Name == "BTN_Bouton_actif");

            check(renamedState.State == WindevControlState.Inactive, "State follows bytes rather than the control name");

            changed = window.PropertyBuffers[0].Data.ToArray();
            changed[3969] = 0xff;
            WriteProperties(window, changed, temporaryFile);

            WindevUiElement damagedHeader = WdwWindevWindow.Open(temporaryFile).Elements.Single(element => element.Name == "BTN_Bouton_isible");

            check(damagedHeader.IsVisible == true && damagedHeader.State is null && damagedHeader.IsTabStop is null, "Unrecognized optional header text stops dependent parsing");

            changed = window.PropertyBuffers[0].Data.ToArray();
            changed[4006] = 0xff;
            WriteProperties(window, changed, temporaryFile);

            WindevUiElement damagedValue = WdwWindevWindow.Open(temporaryFile).Elements.Single(element => element.Name == "BTN_Bouton_isible");

            check(damagedValue.State == WindevControlState.Active && damagedValue.IsTabStop is null, "Unrecognized value text does not fabricate a Tab setting");

            changed = window.PropertyBuffers[0].Data.Span[..4012].ToArray();
            WriteProperties(window, changed, temporaryFile);

            WindevUiElement truncated = WdwWindevWindow.Open(temporaryFile).Elements.Single(element => element.Name == "BTN_Bouton_isible");

            check(truncated.State == WindevControlState.Active && truncated.IsTabStop is null, "Truncated Tab word is never read past the decoded buffer");

            foreach ((int Offset, string Text, int StateShift, int TabShift) insertion in new (int, string, int, int)[]
            {
                (3969, "Header", 18, 18),
                (3976, "Hint", 0, 16),
                (4006, "Value", 0, 17)
            })
            {
                changed = InsertTextSlot(window.PropertyBuffers[0].Data.ToArray(), insertion.Offset, insertion.Text);
                WriteProperties(window, changed, temporaryFile);

                WindevUiElement relocated = WdwWindevWindow.Open(temporaryFile).Elements.Single(element => element.Name == "BTN_Bouton_isible");

                check(relocated.State == WindevControlState.Active && relocated.Interaction?.StateOffset == 3972 + insertion.StateShift, "Optional text framing relocates the state cursor correctly");
                check(relocated.IsTabStop == true && relocated.Interaction?.TabAccessOffset == 4010 + insertion.TabShift, "Optional text framing relocates the Tab cursor correctly");
            }
        }
        finally
        {
            File.Delete(temporaryFile);
        }
    }

    private static void VerifyVersion31(string originalRoot, Action<bool, string> check)
    {
        WdwWindevWindow journal = WdwWindevWindow.Open(Path.Combine(originalRoot, "Origamide/FEN_Journal.wdw"));
        WindevUiElement search = journal.Elements.Single(element => element.Name == "BTN_Recherche");
        WindevUiElement image = journal.Elements.Single(element => element.Name == "IMG_StatutTermine");

        check(search.State == WindevControlState.Active && search.Interaction?.StateOffset == 127461, "Version 31 context-menu text moves state beyond the simple R+47 offset");
        check(search.IsTabStop == true && search.Interaction?.TabAccessOffset == 127535, "Version 31 tooltip and descriptor additions relocate Tab access");
        check(image.IsVisible == false && image.State == WindevControlState.Inactive, "Version 31 independent hidden and inactive values are preserved");
        check(image.IsTabStop == false && image.Interaction?.TabAccessOffset == 86147, "Version 31 image filename is skipped before reading Tab access");
    }

    private static void WriteProperties(WdwWindevWindow window, byte[] properties, string filename)
    {
        File.WriteAllBytes(filename, WindowControlTests.ReplaceProperties(window, properties));
    }

    private static byte[] InsertTextSlot(byte[] source, int offset, string text)
    {
        byte[] textBytes = Encoding.UTF8.GetBytes(text);
        byte[] frame = new byte[textBytes.Length + 13];

        frame[0] = 0xff;
        frame[1] = 0xce;
        frame[2] = 0x88;
        frame[3] = 0xf1;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), (uint)frame.Length);
        textBytes.CopyTo(frame, 8);
        frame[^4] = 0xfe;
        frame[^3] = 0xed;
        frame[^2] = 0x98;
        frame[^1] = 0xe1;

        using MemoryStream output = new MemoryStream();

        output.Write(source.AsSpan(0, offset));
        output.Write(frame);
        output.Write(source.AsSpan(offset + 1));

        return output.ToArray();
    }
}
