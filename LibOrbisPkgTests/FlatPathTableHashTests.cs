using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using LibOrbisPkg.GP4;
using LibOrbisPkg.PFS;
using LibOrbisPkg.PKG;
using LibOrbisPkg.Util;

namespace LibOrbisPkgTests
{
  [TestClass]
  public class FlatPathTableHashTests
  {
    [TestMethod]
    public void FlatPathTableHash_IsCultureInvariant()
    {
      var root = new FSDir();
      root.Files.Add(new FSFile(s => { }, "file.bin", 0) { Parent = root });
      root.Files.Add(new FSFile(s => { }, "FILE.BIN", 0) { Parent = root });
      var saved = Thread.CurrentThread.CurrentCulture;
      try
      {
        // In tr-TR, char.ToUpper('i') is U+0130, not 'I'.
        Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
        Assert.IsTrue(FlatPathTable.HasCollision(root.GetAllChildren()),
          "path hashes must not depend on the build machine's culture");
      }
      finally
      {
        Thread.CurrentThread.CurrentCulture = saved;
      }
    }
  }
}
