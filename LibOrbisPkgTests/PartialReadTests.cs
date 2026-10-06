using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using LibOrbisPkg.PFS;
using LibOrbisPkg.PKG;
using LibOrbisPkg.GP4;

namespace LibOrbisPkgTests
{
  /// <summary>
  /// Stream.Read may legally return fewer bytes than asked. Building through a stream that does
  /// so must give byte-identical output.
  /// </summary>
  [TestClass]
  public class PartialReadTests
  {
    /// <summary>MemoryStream that never returns more than 1000 bytes per Read.</summary>
    class ChunkyStream : MemoryStream
    {
      public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1000));
      public override int Read(Span<byte> buffer) => base.Read(buffer.Slice(0, Math.Min(buffer.Length, 1000)));
    }

    static FSDir Root()
    {
      var root = new FSDir();
      var data = new byte[5 * 0x10000 + 77];
      new Random(1).NextBytes(data);
      root.Files.Add(new FSFile(s => s.Write(data, 0, data.Length), "f.bin", data.Length) { Parent = root });
      return root;
    }

    static PfsProperties Props() => new PfsProperties
    {
      root = Root(), BlockSize = 0x10000, Encrypt = true, Sign = true,
      EKPFS = new byte[32], Seed = new byte[16], FileTime = 1,
    };

    [TestMethod]
    public void SignedEncryptedImage_SameThroughShortReadingStream()
    {
      var normal = new MemoryStream();
      new PfsBuilder(Props()).WriteImage(normal);
      var chunky = new ChunkyStream();
      new PfsBuilder(Props()).WriteImage(chunky);
      CollectionAssert.AreEqual(normal.ToArray(), chunky.ToArray());
    }

    [TestMethod]
    public void Pkg_SameThroughShortReadingStream()
    {
      var props = TestHelper.MakeProperties(VolumeType: VolumeType.pkg_ps4_app);
      var normal = new MemoryStream();
      new PkgBuilder(props).Write(normal, s => { });
      var chunky = new ChunkyStream();
      new PkgBuilder(TestHelper.MakeProperties(VolumeType: VolumeType.pkg_ps4_app)).Write(chunky, s => { });
      CollectionAssert.AreEqual(normal.ToArray(), chunky.ToArray());
    }
  }
}
