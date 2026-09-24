using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class TableNamingTests
    {
        [TestCase("Attack from Mars (Bally 1995)", "Attack from Mars", "Bally", 1995)]
        [TestCase("Big Bang Bar (Capcom 1996) VPW 2.1", "Big Bang Bar", "Capcom", 1996)]
        [TestCase("Star Wars (Data East 1992)", "Star Wars", "Data East", 1992)]
        [TestCase("Simpsons Pinball Party, The (Stern 2003)", "The Simpsons Pinball Party", "Stern", 2003)]
        [TestCase("Spider-Man (Vault Edition) (Stern 2016)", "Spider-Man (Vault Edition)", "Stern", 2016)]
        [TestCase("Attack_from_Mars_(Bally_1995)", "Attack from Mars", "Bally", 1995)]
        [TestCase("2001 (Gottlieb 1971)", "2001", "Gottlieb", 1971)]
        public void Parse_ReadsTitleManufacturerAndYear(string stem, string title, string maker, int year)
        {
            ParsedName name = TableNaming.Parse(stem);
            Assert.AreEqual(title, name.Title);
            Assert.AreEqual(maker, name.Manufacturer);
            Assert.AreEqual(year, name.Year);
        }

        [Test]
        public void Parse_WithoutYearGroup_KeepsWholeNameAsTitle()
        {
            ParsedName name = TableNaming.Parse("Pinball Training Lab");
            Assert.AreEqual("Pinball Training Lab", name.Title);
            Assert.IsNull(name.Manufacturer);
            Assert.AreEqual(0, name.Year);
        }

        [Test]
        public void Normalize_StripsPunctuationCaseAndVrRoomPrefix()
        {
            Assert.AreEqual("medieval madness williams 1997",
                TableNaming.Normalize("VR ROOM Medieval_Madness (Williams  1997)"));
        }

        [TestCase("Big Bang Bar (Capcom 1996) VPW 2.1", "big bang bar capcom 1996")]
        [TestCase("Attack_from_Mars_Bally_1995_VPW", "attack from mars bally 1995")]
        [TestCase("2001 (Gottlieb 1971) Mod", "2001 gottlieb 1971")]
        public void TitleYearSignature_DropsDecoration(string name, string expected)
        {
            Assert.AreEqual(expected, TableNaming.TitleYearSignature(name));
        }
    }
}
