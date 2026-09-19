using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Server.Mobiles;

namespace Server.Commands
{
    /// <summary>
    /// Loads XmlSpawner files like [XmlLoad, but skips every spawner that belongs
    /// to a later expansion than the one running. [CreateWorld uses it for its
    /// Spawners step, so a world generated at an early expansion is not populated
    /// with content from later ones.
    ///
    /// Two gates:
    ///
    ///   facet  a spawner is skipped when its map is later than the running
    ///          expansion (Trammel UOR, Ilshenar UOTD, Malas AOS, Tokuno SE,
    ///          Ter Mur SA). Per spawner rather than per file, because
    ///          trammel.xml also carries the Khaldun spawners, which are on
    ///          Felucca.
    ///   file   a file listed in FileEras is skipped whole when its content is
    ///          later than its facet: Twisted Weald is on Ilshenar but ML,
    ///          Gravewater Lake on Trammel but SA, Eodon on Ter Mur but TOL.
    ///
    /// A file with no FileEras entry gets only the facet gate, so a shard's own
    /// spawn files load as they did under [XmlLoad.
    /// </summary>
    public static class XmlLoadEra
    {
        private static readonly Dictionary<string, Expansion> FileEras =
            new Dictionary<string, Expansion>(StringComparer.OrdinalIgnoreCase)
            {
                { "solenhives.xml", Expansion.LBR },
                { "twistedweald.xml", Expansion.ML },
                { "GravewaterLake.xml", Expansion.SA },
                { "TheExodusEncounterQuest.xml", Expansion.SA },
                { "Eodon.xml", Expansion.TOL },
                { "TreasuresOfKotl.xml", Expansion.TOL },
            };

        public static void Initialize()
        {
            CommandSystem.Register("XmlLoadEra", XmlSpawner.DiskAccessLevel, XmlLoadEra_OnCommand);
        }

        /// <summary>The expansion that introduced a facet, by its name in a spawn file.</summary>
        public static Expansion MapEra(string map)
        {
            switch ((map ?? String.Empty).Trim().ToLowerInvariant())
            {
                case "trammel": return Expansion.UOR;
                case "ilshenar": return Expansion.UOTD;
                case "malas": return Expansion.AOS;
                case "tokuno": return Expansion.SE;
                case "termur": return Expansion.SA;
                default: return Expansion.None;
            }
        }

        [Usage("XmlLoadEra <SpawnFile or directory>")]
        [Description("Loads XmlSpawner files like XmlLoad, skipping spawners on facets, and files of content, from later expansions than the one running.")]
        private static void XmlLoadEra_OnCommand(CommandEventArgs e)
        {
            if (e.Arguments.Length < 1)
            {
                e.Mobile.SendMessage("Usage: {0} <SpawnFile or directory>", e.Command);
                return;
            }

            var path = e.Arguments[0];

            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(Core.BaseDirectory, path);
            }

            string[] files;

            if (Directory.Exists(path))
            {
                files = Directory.GetFiles(path, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            else if (File.Exists(path))
            {
                files = new[] { path };
            }
            else
            {
                e.Mobile.SendMessage("{0} was not found.", path);
                return;
            }

            int loaded = 0, skipped = 0;

            foreach (var file in files)
            {
                Expansion fileEra;

                if (FileEras.TryGetValue(Path.GetFileName(file), out fileEra) && Core.Expansion < fileEra)
                {
                    Console.WriteLine("XmlLoadEra: skipped {0} ({1} content)", Path.GetFileName(file), fileEra);
                    continue;
                }

                var doc = XDocument.Load(file);
                var root = doc.Root;

                if (root == null)
                {
                    continue;
                }

                var late = root.Elements("Points").Where(p => Core.Expansion < MapEra((string)p.Element("Map"))).ToList();

                foreach (var p in late)
                {
                    p.Remove();
                }

                skipped += late.Count;

                using (var ms = new MemoryStream())
                {
                    doc.Save(ms);
                    ms.Position = 0;

                    int maps, spawners;

                    XmlSpawner.XmlLoadFromStream(ms, file, String.Empty, e.Mobile, e.Mobile.Location, e.Mobile.Map, false, 0, false, out maps, out spawners);

                    loaded += spawners;
                }
            }

            e.Mobile.SendMessage("XmlLoadEra: {0} spawners loaded, {1} skipped as later than {2}.", loaded, skipped, Core.Expansion);
        }
    }
}
