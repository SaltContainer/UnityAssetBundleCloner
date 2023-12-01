using AssetsTools.NET;
using AssetsTools.NET.Extra;
using SmartPoint.AssetAssistant;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace UnityAssetBundleCloner
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            string outputDir = Environment.CurrentDirectory + "\\Output";
            string tempDir = Environment.CurrentDirectory + "\\Temp";
            Random rng = new();

            OpenFileDialog aaofd = new()
            {
                Filter = "AssetAssistant Objects(*.bin)|*.bin",
                RestoreDirectory = true
            };
            if (aaofd.ShowDialog() != DialogResult.OK) return;
            string assetAssistantPath = aaofd.FileName;
            string assetAssistantDir = Path.GetDirectoryName(assetAssistantPath) ?? throw new ArgumentException("Not a path.");

            OpenFileDialog abofd = new()
            {
                Filter = "AssetBundle(*.*)|*.*",
                RestoreDirectory = true
            };
            if (abofd.ShowDialog() != DialogResult.OK) return;
            string mainAssetBundlePath = abofd.FileName;
            string mainAssetBundleDir = mainAssetBundlePath.Split("AssetAssistant\\").Last();
            string projectName = mainAssetBundleDir.Split('\\').First();
            string mainAssetBundleName = mainAssetBundleDir[(projectName.Length + 1)..];

            OpenFileDialog csvofd = new()
            {
                Filter = "Comma-separated values file(*.csv)|*.csv",
                RestoreDirectory = true
            };
            if (csvofd.ShowDialog() != DialogResult.OK) return;
            string csvPath = csvofd.FileName;
            List<string> listOfNames = GetListOfNames(csvPath);

            string currentBundle = "";

            AssetBundleDownloadManifest abdm = AssetBundleDownloadManifest.Load(assetAssistantPath);
            HashSet<string> assetPaths = abdm.records.SelectMany(abr => abr.assetPaths).ToHashSet();
            AssetBundleRecord abr = abdm.GetAssetBundleRecord(mainAssetBundleName.Replace('\\', '/'));
            AssetsManager am = new();
            BundleFileInstance bfi = am.LoadBundleFile(assetAssistantDir + "\\" + projectName + "\\" + abr.assetBundleName);
            DecompressBundle(bfi);
            AssetsFileInstance afi = am.LoadAssetsFileFromBundle(bfi, 0);

            try
            {
                foreach (string name in listOfNames)
                {
                    currentBundle = name;

                    string newAssetBundleName = name;
                    string newCab = GenCABName(rng);

                    AssetBundleRecord newRecord = (AssetBundleRecord)abr.Clone();
                    newRecord.assetBundleName = newAssetBundleName;


                    // Adjust Asset Path names
                    //for (int j = 0; j < newRecord.assetPaths.Length; j++)
                    //{
                    string[] oldPathParts = newRecord.assetPaths[0].Split("/");
                    string bundleName = oldPathParts.Last();
                    string newPath = string.Join("/", oldPathParts.Take(oldPathParts.Length - 1));
                    string finalPath = newPath + "/" + newAssetBundleName.Split("/").Last() + ".prefab";
                    newRecord.assetPaths[0] = finalPath;
                    //}

                    abdm.Add(newRecord);


                    // Rename CAB
                    List<AssetsReplacer> ars = new();
                    string oldCAB = afi.name.Replace("CAB-", "");
                    afi.name = afi.name.Replace(oldCAB, newCab);
                    foreach (AssetBundleDirectoryInfo06 abdi6 in bfi.file.bundleInf6.dirInf)
                        abdi6.name = abdi6.name.Replace(oldCAB, newCab);


                    // Adjust Texture2Ds
                    List<AssetTypeValueField> texture2Ds = afi.table.GetAssetsOfType((int)AssetClassID.Texture2D).Select(afie => am.GetTypeInstance(afi, afie).GetBaseField()).ToList();
                    foreach (AssetTypeValueField texture2DField in texture2Ds)
                    {
                        string m_Name = texture2DField["m_Name"].value.AsString();
                        AssetFileInfoEx afie = afi.table.GetAssetInfo(m_Name, (int)AssetClassID.Texture2D);

                        string m_StreamDataPath = texture2DField["m_StreamData"].children[2].value.AsString();
                        m_StreamDataPath = m_StreamDataPath.Replace(oldCAB, newCab);
                        texture2DField["m_StreamData"].children[2].GetValue().Set(m_StreamDataPath);

                        byte[] b = texture2DField.WriteToByteArray();
                        AssetsReplacerFromMemory arfm = new(0, afie.index, (int)afie.curFileType, AssetHelper.GetScriptIndex(afi.file, afie), b);
                        if (!ars.Select(x => x.GetPathID()).Contains(afie.index))
                            ars.Add(arfm);
                    }

                    am.UpdateDependencies(afi);


                    // Adjust Container stuff
                    AssetTypeValueField assetBundleFile = am.GetTypeInstance(afi, afi.table.GetAssetInfo(1)).GetBaseField();
                    string abf_m_Name = assetBundleFile["m_Name"].value.AsString();
                    AssetFileInfoEx abf_afie = afi.table.GetAssetInfo(abf_m_Name, (int)AssetClassID.AssetBundle);

                    assetBundleFile["m_Name"].GetValue().Set(newAssetBundleName);
                    assetBundleFile["m_Container"]["Array"][0]["first"].GetValue().Set(finalPath);
                    assetBundleFile["m_AssetBundleName"].GetValue().Set(newAssetBundleName);

                    byte[] abf_b = assetBundleFile.WriteToByteArray();
                    AssetsReplacerFromMemory abf_arfm = new(0, abf_afie.index, (int)abf_afie.curFileType, AssetHelper.GetScriptIndex(afi.file, abf_afie), abf_b);
                    ars.Add(abf_arfm);


                    // Adjust main GameObject stuff
                    AssetTypeValueField mainGO = am.GetTypeInstance(afi, afi.table.GetAssetInfo(-6933693207961869576)).GetBaseField();
                    string go_m_Name = mainGO["m_Name"].value.AsString();
                    AssetFileInfoEx go_afie = afi.table.GetAssetInfo(go_m_Name, (int)AssetClassID.GameObject);

                    mainGO["m_Name"].GetValue().Set(newAssetBundleName.Split("/").Last());

                    byte[] go_b = mainGO.WriteToByteArray();
                    AssetsReplacerFromMemory go_arfm = new(0, go_afie.index, (int)go_afie.curFileType, AssetHelper.GetScriptIndex(afi.file, go_afie), go_b);
                    ars.Add(go_arfm);


                    // Saving
                    MemoryStream memoryStream = new MemoryStream();
                    AssetsFileWriter afw = new(memoryStream);
                    afi.file.dependencies.Write(afw);
                    afi.file.Write(afw, 0, ars, 0);
                    BundleReplacerFromMemory brfm = new(bfi.file.bundleInf6.dirInf[0].name, "CAB-" + newCab, true, memoryStream.ToArray(), -1);

                    string tempAssetBundlePath = tempDir + "\\" + newRecord.projectName + "\\" + newRecord.assetBundleName;
                    string outputAssetBundlePath = outputDir + "\\" + newRecord.projectName + "\\" + newRecord.assetBundleName;
                    Directory.CreateDirectory(Path.GetDirectoryName(tempAssetBundlePath) ?? throw new ArgumentException("Not a path."));
                    Directory.CreateDirectory(Path.GetDirectoryName(outputAssetBundlePath) ?? throw new ArgumentException("Not a path."));

                    if (File.Exists(outputAssetBundlePath))
                        File.Delete(outputAssetBundlePath);

                    var bfi_stream = File.OpenWrite(outputAssetBundlePath + "_unc");
                    afw = new(bfi_stream);
                    bfi.file.Write(afw, new List<BundleReplacer> { brfm });
                    afw.Close();

                    /*am = new();
                    BundleFileInstance output_bfi = am.LoadBundleFile(tempAssetBundlePath, false);
                    if (File.Exists(outputAssetBundlePath))
                        File.Delete(outputAssetBundlePath);

                    var output_bfi_stream = File.OpenWrite(outputAssetBundlePath);
                    afw = new AssetsFileWriter(output_bfi_stream);
                    output_bfi.file.Pack(bfi.file.reader, afw, AssetBundleCompressionType.LZ4);
                    afw.Close();
                    bfi_stream.Close();
                    bfi.file.Close();
                    bfi.BundleStream.Dispose();

                    output_bfi_stream.Close();
                    output_bfi.file.Close();
                    output_bfi.BundleStream.Dispose();
                    File.Delete(tempAssetBundlePath);*/
                }

                abdm.Save(outputDir + "\\" + Path.GetFileName(assetAssistantPath));
                MessageBox.Show("Results placed in Output folder.", "Success!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("An exception occured when cloning into {0}! Full message: {1}", currentBundle, ex.Message), "Exception occured!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string IncrementName(string s)
        {
            Match m = Regex.Match(s, @"\d+\z");
            if (!m.Success)
                return s + "0";
            return s[..(s.Length - m.Value.Length)] + (int.Parse(m.Value) + 1);
        }

        private static void DecompressBundle(BundleFileInstance bfi)
        {
            AssetBundleFile abf = bfi.file;

            MemoryStream stream = new();
            abf.Unpack(abf.reader, new AssetsFileWriter(stream));

            stream.Position = 0;

            AssetBundleFile newAbf = new();
            newAbf.Read(new AssetsFileReader(stream), false);

            abf.reader.Close();
            bfi.file = newAbf;
        }

        private static string GenCABName(Random rng)
        {
            string[] values = new string[4];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = rng.Next().ToString("x8");
            }

            return string.Join("", values);
        }

        private static List<string> GetListOfNames(string file)
        {
            List<string> list = new List<string>();

            using (var reader = new StreamReader(file))
            {

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();

                    list.Add(line);
                }
            }

            return list;
        }
    }
}