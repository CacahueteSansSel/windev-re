using WindevReader;

internal static class Wda15Tests
{
    internal static void Run(
        string filename,
        Action<bool, string> check,
        Action<Action, string> reject,
        List<string> checks)
    {
        WdaWindevAnalysisSource analysis = WdaWindevAnalysisSource.Open(filename);

        check(analysis.Header.WindevVersion == 15 && analysis.Header.RawDocumentType == 0x80002, "Version 15 editable analysis identified");
        check(analysis.Name == Path.GetFileNameWithoutExtension(filename), "Unmapped WDA identity falls back to the filename");
        check(analysis.RawData.Length == 374 && analysis.Header.PayloadOffset == 137, "Version 15 WDA byte coverage");
        check(analysis.Sections.Select(section => section.Offset).SequenceEqual(new int[] { 137, 295, 295, 307 }), "Version 15 WDA section boundaries");
        check(analysis.Sections.Select(section => section.ByteLength).SequenceEqual(new int[] { 158, 0, 12, 67 }), "Empty graphical section is preserved");
        check(analysis.Sections.All(section => section.DecodedBlocks.Count == 0), "Raw WDA regions are not presented as decompressed buffers");
        check(analysis.CodeBlocks.Count == 0, "WDA auxiliary sections are not source code");
        check(analysis.Diagnostics.Any(diagnostic => diagnostic.Code == "LowLevelAnalysisSource"), "WDA semantic limitations remain explicit");
        check(!analysis.Diagnostics.Any(diagnostic => diagnostic.Code == "UntestedVersion"), "Supplied WDA 15 accepted without an opt-in");
        check(WindevFile.Open(filename) is WdaWindevAnalysisSource, "WDA signature dispatch");
        reject(() => WdaWindevAnalysisSource.Open(filename, new WindevOpenOptions { MaxFileBytes = 373 }), "WDA file budget enforced");

        string temporaryFile = Path.Combine(Path.GetTempPath(), "wda15-" + Guid.NewGuid().ToString("N") + ".wda");

        try
        {
            File.WriteAllBytes(temporaryFile, analysis.RawData.Span[..373].ToArray());
            reject(() => WdaWindevAnalysisSource.Open(temporaryFile), "Truncated WDA section rejected");

            byte[] changed = analysis.RawData.ToArray();

            changed[14] = 24;
            File.WriteAllBytes(temporaryFile, changed);
            reject(() => WdaWindevAnalysisSource.Open(temporaryFile), "WDA 24 is not inferred from WDA 15");
        }
        finally
        {
            File.Delete(temporaryFile);
        }

        checks.Add("WDA 15: four raw sections, including an empty graphical section; filename identity fallback; no LZW buffer or typed schema inferred; truncation and unsupported version refused.");
    }
}
