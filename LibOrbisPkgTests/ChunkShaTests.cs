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
  public class ChunkShaTests
  {
    const int Block = 0x10000;

    [TestMethod, TestCategory("Slow")]
    public void ChunkShaAllocation_HoldsForAnyPfsSize()
    {
      // BuildPkg pre-allocates the PlayGo chunk hash entry from an estimate; the entry itself can
      // push the body across a 0x80000 boundary. Sweep sizes where that happens (multi-GB pkgs)
      // without writing anything.
      var props = TestHelper.MakeProperties(VolumeType: VolumeType.pkg_ps4_app);
      var builder = new PkgBuilder(props);
      var inner = new PfsBuilder(PfsProperties.MakeInnerPFSProps(props));
      typeof(PkgBuilder).GetField("innerPfs", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(builder, inner);
      var failures = new List<(long pfs, string error)>();
      for (long pfs = 1L << 30; pfs < 70L << 30; pfs += 37L * Block + 0x1234)
      {
        try { builder.BuildPkg(pfs); }
        catch (Exception ex) { failures.Add((pfs, ex.Message)); }
      }
      Assert.AreEqual(0, failures.Count,
        $"failed for {failures.Count} sizes, first {failures.FirstOrDefault().pfs}: {failures.FirstOrDefault().error}");
    }
  }
}
