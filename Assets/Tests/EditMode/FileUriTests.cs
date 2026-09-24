using System;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class FileUriTests
    {
        [TestCase(@"C:\Media\Tables\Attack from Mars (Bally 1995)\wheel.png")]
        [TestCase(@"C:\Media\Tables\Bram Stoker's Dracula (Williams 1993)\bg.png")]
        [TestCase(@"C:\Media\Tables\Simpsons Pinball Party, The (Stern 2003)\table.png")]
        [TestCase(@"C:\Media\Tables\Rock & Roll (Bally 1990)\table.png")]
        [TestCase(@"C:\Media\Tables\100% Pinball (Foo 1990)\table.png")]
        [TestCase(@"C:\Media\Tables\Pin #1 (Foo 1990)\wheel.png")]
        public void FromPath_RoundTripsThroughUri(string path)
        {
            string uri = FileUri.FromPath(path);
            StringAssert.StartsWith("file:///C:/Media/Tables/", uri);
            StringAssert.DoesNotContain(" ", uri);
            StringAssert.DoesNotContain("#", uri);
            Assert.AreEqual(path, new Uri(uri).LocalPath);
        }
    }
}
