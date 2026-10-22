using System.Buffers.Binary;
using WindevReader;

internal static class WindowControlTests
{
    internal static void Run(
        WdwWindevWindow window,
        Action<bool, string> check,
        List<string> checks)
    {
        (string Name, WindevBounds Bounds, string Caption)[] expected =
        [
            ("SAI_Nom", new WindevBounds(24, 24, 272, 31), "Nom"),
            ("SAI_Prenom", new WindevBounds(24, 64, 272, 31), "Prenom"),
            ("SAI_Desc", new WindevBounds(24, 104, 504, 70), "Desc"),
            ("BTN_SansNom1", new WindevBounds(24, 188, 106, 31), "créé"),
            ("BTN_SansNom2", new WindevBounds(140, 188, 106, 31), "ouvre")
        ];

        foreach ((string Name, WindevBounds Bounds, string Caption) control in expected)
        {
            WindevUiElement element = window.Elements.Single(candidate => candidate.Name == control.Name);

            check(element.Bounds == control.Bounds, "Control rectangle agrees with the supplied design and companion controls");
            check(element.Caption == control.Caption, "Control caption comes from its structural position");
            check(element.IsVisible == true && element.State == WindevControlState.Active && element.IsTabStop == true, "Version 29 initial settings corroborate the supplied user descriptions");
            check(element.Layout?.RawDescriptor == 607, "Control layout requires the observed descriptor");
        }

        WindevUiElement input = window.Elements.Single(element => element.Name == "SAI_Nom");
        WindevUiElement button = window.Elements.Single(element => element.Name == "BTN_SansNom1");

        check(input.Layout?.BoundsOffset == 4198 && input.Layout.CaptionOffset == 4266, "Input offsets use decoded-buffer coordinates");
        check(button.Layout?.BoundsOffset == 11796 && button.Layout.CaptionOffset == 12060, "Button caption uses its separate field");
        check(input.Layout?.CommonTailOffset == 4282 && button.Layout?.CommonTailOffset == 11865, "Variable-length captions shift the common tail");
        check(input.Layout?.RawValues.Single(value => value.Offset == 4230).Value == 1, "Raw header value is exposed without a visibility interpretation");
        check(input.Layout?.RawValues.Single(value => value.Offset == 4306).Value == 0, "First navigation candidate remains a raw integer");
        check(input.Layout?.RawValues.Single(value => value.Offset == 4311).Value == 1, "Second navigation candidate remains a raw integer");

        string temporaryFile = Path.Combine(Path.GetTempPath(), "window-controls-" + Guid.NewGuid().ToString("N") + ".wdw");

        try
        {
            byte[] properties = window.PropertyBuffers[0].Data.ToArray();

            BinaryPrimitives.WriteInt32LittleEndian(properties.AsSpan(4198), -24);
            BinaryPrimitives.WriteInt32LittleEndian(properties.AsSpan(4202), 500);
            BinaryPrimitives.WriteInt32LittleEndian(properties.AsSpan(4206), 300);
            BinaryPrimitives.WriteInt32LittleEndian(properties.AsSpan(4210), 40);
            "ABC"u8.CopyTo(properties.AsSpan(4274));
            File.WriteAllBytes(temporaryFile, ReplaceProperties(window, properties));

            WindevUiElement changed = WdwWindevWindow.Open(temporaryFile).Elements.Single(element => element.Name == "SAI_Nom");

            check(changed.Bounds == new WindevBounds(-24, 500, 300, 40), "Coordinates are read dynamically and retain their sign");
            check(changed.Caption == "ABC", "Changed caption is read instead of an expected fixture value");

            properties = window.PropertyBuffers[0].Data.ToArray();
            window.PropertyBuffers[0].Data.Span.Slice(4170, 20).CopyTo(properties.AsSpan(2000));
            File.WriteAllBytes(temporaryFile, ReplaceProperties(window, properties));

            changed = WdwWindevWindow.Open(temporaryFile).Elements.Single(element => element.Name == "SAI_Nom");

            check(changed.Bounds == input.Bounds && changed.Offset == 4170, "An earlier name reference cannot hide the recognized control descriptor");

            properties = window.PropertyBuffers[0].Data.Span[..4190].ToArray();
            File.WriteAllBytes(temporaryFile, ReplaceProperties(window, properties));

            changed = WdwWindevWindow.Open(temporaryFile).Elements.Single(element => element.Name == "SAI_Nom");

            check(changed.Bounds is null && changed.Layout is null, "Truncated control body retains the name without reading past its buffer");

            properties = window.PropertyBuffers[0].Data.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(4166), 608);
            File.WriteAllBytes(temporaryFile, ReplaceProperties(window, properties));

            changed = WdwWindevWindow.Open(temporaryFile).Elements.Single(element => element.Name == "SAI_Nom");

            check(changed.Bounds is null && changed.Layout is null, "Unknown descriptor retains the element but does not guess its layout");

            properties = window.PropertyBuffers[0].Data.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(properties.AsSpan(4206), -1);
            File.WriteAllBytes(temporaryFile, ReplaceProperties(window, properties));

            changed = WdwWindevWindow.Open(temporaryFile).Elements.Single(element => element.Name == "SAI_Nom");

            check(changed.Bounds is null, "Negative dimensions do not become a recognized rectangle");

            properties = window.PropertyBuffers[0].Data.ToArray();
            properties[4266] = 0x7f;
            File.WriteAllBytes(temporaryFile, ReplaceProperties(window, properties));

            changed = WdwWindevWindow.Open(temporaryFile).Elements.Single(element => element.Name == "SAI_Nom");

            check(changed.Bounds == input.Bounds && changed.Caption is null && changed.Layout?.CommonTailOffset is null, "Damaged caption framing does not shift parsing into arbitrary bytes");

            properties = window.PropertyBuffers[0].Data.ToArray();
            properties[12060] = 0;
            File.WriteAllBytes(temporaryFile, ReplaceProperties(window, properties));

            changed = WdwWindevWindow.Open(temporaryFile).Elements.Single(element => element.Name == "BTN_SansNom1");

            check(changed.Caption is null, "Unsupported button caption is not replaced by a nearby style name");
        }
        finally
        {
            File.Delete(temporaryFile);
        }

        checks.Add("Window controls: five version 29 rectangles/captions; decoded offsets and raw numeric metadata; changed signed coordinates/caption, unsupported descriptor, invalid dimensions and damaged caption framing. Visibility, interaction state and Tab access are decoded in compatible 28/29/31 layouts. Opacity and input type remain unassigned.");
    }

    internal static byte[] ReplaceProperties(WdwWindevWindow window, byte[] properties)
    {
        byte[] encoded = EncodeLiteralLzw(properties);
        byte[] section = new byte[39 + encoded.Length];

        window.Sections[1].RawData.Span[..28].CopyTo(section);
        section[28] = 4;
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(29), (uint)encoded.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(33), (uint)properties.Length);
        encoded.CopyTo(section, 39);

        byte[] header = window.RawData.Span[..window.Header.PayloadOffset].ToArray();

        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(91), (uint)section.Length);

        using MemoryStream output = new MemoryStream();

        output.Write(header);

        foreach (WindevSection original in window.Sections)
        {
            output.Write(original.Index == 1 ? section : original.RawData.Span);
        }

        return output.ToArray();
    }

    private static byte[] EncodeLiteralLzw(byte[] source)
    {
        byte[] encoded = new byte[((source.Length * 2 + 1) * 9 + 7) / 8];
        int bitOffset = 0;

        foreach (byte value in source)
        {
            WriteCode(encoded, ref bitOffset, 0);
            WriteCode(encoded, ref bitOffset, value + 1);
        }

        WriteCode(encoded, ref bitOffset, 257);

        return encoded;
    }

    private static void WriteCode(byte[] output, ref int bitOffset, int code)
    {
        for (int bitIndex = 0; bitIndex < 9; bitIndex++, bitOffset++)
        {
            output[bitOffset / 8] |= (byte)(((code >> bitIndex) & 1) << (bitOffset % 8));
        }
    }
}
