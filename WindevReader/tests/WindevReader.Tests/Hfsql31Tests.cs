using System.Buffers.Binary;
using WindevReader;

internal static class Hfsql31Tests
{
    internal static void Run(string directory, string originalRoot, Action<bool,string> check, Action<Action,string> reject, List<string> checks)
    {
        var analysis = WddWindevAnalysis.Open(Path.Combine(originalRoot,"Origamide/Origamide.wdd"));
        var holiday = FicHfsqlDataFile.Open(Path.Combine(directory,"jour_ferie.fic"));
        var weeks = FicHfsqlDataFile.Open(Path.Combine(directory,"semaine_cours.fic"));
        var hi = NdxHfsqlIndexFile.Open(Path.Combine(directory,"jour_ferie.ndx"));
        var wi = NdxHfsqlIndexFile.Open(Path.Combine(directory,"semaine_cours.ndx"));
        check(holiday.HasSameIdentityAs(hi) && weeks.HasSameIdentityAs(wi),"31 companion GUIDs");
        check(holiday.MatchesAnalysis(analysis) && weeks.MatchesAnalysis(analysis),"New files match original WDD 31");
        var ht = holiday.FindTableDefinition(analysis)!;
        check(ht.RawRecordByteLength == 70 && ht.Fields.Single(f=>f.Name=="MOISJOUR").IsCompositeKey,"Composite key metadata and actual slot length");
        check(ht.Fields.Single(f=>f.Name=="MOISJOUR").Type == "CompositeKey","Refined composite type label");
        check(!analysis.Tables.SelectMany(t=>t.Fields).Single(f=>f.Name=="HASH").IsCompositeKey,"Ordinary binary field with the same flags is not a composed key");
        var hLayout = holiday.CreateFixedRecordLayout(analysis,1763,8);
        var wLayout = weeks.CreateFixedRecordLayout(analysis,1627,1);
        check(hLayout.RecordByteLength == 70 && hLayout.Fields.Count == 4 && !hLayout.Fields.Any(f=>f.Name=="MOISJOUR"),"Composite key consumes no FIC bytes");
        check(hLayout.Fields.Select(f=>f.Offset).SequenceEqual(new[]{9,17,18,19}),"31 holiday physical field offsets");
        check(wLayout.RecordByteLength == 35 && wLayout.Fields.Select(f=>f.Offset).SequenceEqual(new[]{9,17,26}),"31 week date offsets");
        var rows = holiday.ReadFixedRecords(hLayout); var weekRows = weeks.ReadFixedRecords(wLayout);
        byte[] days = [1,1,8,14,15,1,11,25], months = [1,5,5,7,8,11,11,12];
        for (int i=0;i<8;i++)
        {
            check((long)rows[i].Values["IDJOUR_FERIE"] == i+1,"31 FIC IDs");
            check((byte)rows[i].Values["JOUR"] == days[i] && (byte)rows[i].Values["MOIS"] == months[i],"Unsigned one-byte values");
        }
        check((string)rows[3].Values["LIBELLE"] == "Fête nationale" && (string)rows[7].Values["LIBELLE"] == "Noël","Clear CP1252 accents in 31 records");
        check((string)weekRows[0].Values["DATEDEB"] == "20260504" && (string)weekRows[0].Values["DATEFIN"] == "20260508","Date8Z values preserved as yyyyMMdd strings");
        check(hi.DirectoryOffset==9971 && hi.IndexDescriptorOffsets.SequenceEqual(new[]{135,2893,5221,7549}),"31 holiday terminal directory");
        check(wi.DirectoryOffset==8529 && wi.IndexDescriptorOffsets.SequenceEqual(new[]{135,2893,5711}),"31 week terminal directory");
        for (int j=0;j<4;j++)
        {
            var page = hi.ReadFlatIndex(j);
            check(page.Entries.Count==8 && page.DeclaredEntryCount==9,"31 flat page includes one terminal sentinel");
            foreach (var entry in page.Entries)
            {
                check(entry.RawReference1==entry.RawReference2 && entry.RawReference1 is >=1 and <=8,"Two raw references agree with both ID and physical ordinal in these witnesses");
                int i=(int)entry.RawReference1-1;
                bool agrees=j switch {
                    0 => (BinaryPrimitives.ReadUInt64BigEndian(entry.Key.Span)^0x8000000000000000UL)==(ulong)(i+1),
                    1 => entry.Key.Span[0]==days[i],
                    2 => entry.Key.Span[0]==months[i],
                    3 => entry.Key.Span.SequenceEqual(new[]{months[i],days[i]}),
                    _ => false };
                check(agrees,"Every flat key matches independent FIC values");
            }
        }
        for(int j=0;j<3;j++)
        {
            var page = wi.ReadFlatIndex(j);
            check(page.Entries.Count==1 && page.DeclaredEntryCount==2,"31 single-row page and sentinel");
            if(j>0)check(System.Text.Encoding.ASCII.GetString(page.Entries[0].Key.Span)==(string)weekRows[0].Values[j==1?"DATEDEB":"DATEFIN"]+"\0","Date keys match FIC date strings with terminator");
        }
        string temp=Path.Combine(Path.GetTempPath(),"hfsql31-"+Guid.NewGuid().ToString("N")+".bin");
        try
        {
            byte[] b=hi.RawData.ToArray();b[hi.ReadFlatIndex(0).TerminalSentinelOffset]=0;File.WriteAllBytes(temp,b);
            reject(()=>NdxHfsqlIndexFile.Open(temp).ReadFlatIndex(0),"Corrupt flat terminal sentinel");
            b=hi.RawData.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(199),uint.MaxValue);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(204),uint.MaxValue);File.WriteAllBytes(temp,b);
            reject(()=>NdxHfsqlIndexFile.Open(temp).ReadFlatIndex(0),"Unbounded flat count rejected");
            b=hi.RawData.ToArray();b[224]=0xFF;File.WriteAllBytes(temp,b);
            reject(()=>NdxHfsqlIndexFile.Open(temp).ReadFlatIndex(0),"Out-of-order flat key rejected");
            b=weeks.RawData.ToArray();b[1644]=(byte)'X';File.WriteAllBytes(temp,b);
            reject(()=>FicHfsqlDataFile.Open(temp).ReadFixedRecords(wLayout)[0].ToString(),"Non-digit Date8Z rejected");
            b=weeks.RawData.ToArray();b[1652]=1;File.WriteAllBytes(temp,b);
            reject(()=>FicHfsqlDataFile.Open(temp).ReadFixedRecords(wLayout)[0].ToString(),"Non-terminated Date8Z rejected");
        }
        finally{File.Delete(temp);}
        reject(()=>NdxHfsqlIndexFile.Open(hi.FileName,new(){MaxIndexEntries=2}).ReadFlatIndex(0),"Flat key allocation budget");
        var rad=NdxHfsqlIndexFile.Open(Path.Combine(originalRoot,"Origamide/RADFic.ndx"));
        reject(()=>rad.ReadFlatIndex(0),"Opaque RAD index is not accepted as a clear flat page");
        checks.Add("HFSQL 31: both FICs matched WDD; eight holiday slots plus one week slot; compound key omitted from FIC; 7 flat NDX pages, 35 real keys and 7 sentinels; every key cross-checked against FIC; date strings, CP1252, corruption and budgets verified.");
    }
}
