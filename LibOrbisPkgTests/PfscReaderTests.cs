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
  public class PfscReaderTests
  {
    const int Block = 0x10000;

    static byte[] MakePfsc(byte[] data, Func<int, bool> compressBlock)
    {
      int n = (data.Length + Block - 1) / Block;
      long table = 8 + n * 8L;
      long extra = ((table - 0xFC00) + 0xFFFF) / 0x10000;
      long hdrSize = 0x10000 + (extra > 0 ? Block * extra : 0);
      var blocks = new List<byte[]>();
      for (int i = 0; i < n; i++)
      {
        var raw = new byte[Block];
        Buffer.BlockCopy(data, i * Block, raw, 0, Math.Min(Block, data.Length - i * Block));
        if (compressBlock(i))
        {
          using (var ms = new MemoryStream())
          {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true))
              z.Write(raw, 0, raw.Length);
            blocks.Add(ms.ToArray());
          }
        }
        else blocks.Add(raw);
      }
      using (var o = new MemoryStream())
      using (var w = new BinaryWriter(o))
      {
        w.Write(new byte[] { (byte)'P', (byte)'F', (byte)'S', (byte)'C' });
        w.Write(0); w.Write(2); w.Write(Block); w.Write((long)Block);
        w.Write(0x400L); w.Write(hdrSize); w.Write((long)n * Block);
        o.Position = 0x400;
        long off = hdrSize;
        foreach (var b in blocks) { w.Write(off); off += b.Length; }
        w.Write(off);
        o.Position = hdrSize;
        foreach (var b in blocks) w.Write(b);
        return o.ToArray();
      }
    }

    [TestMethod]
    public void PfscReader_DecompressesMixedBlocksExactly()
    {
      // Highly compressible but non-trivial data: forces long deflate streams per block.
      var data = new byte[40 * Block + 1234];
      var rnd = new Random(7);
      for (int i = 0; i < data.Length; i++)
        data[i] = (byte)((i / 3) % 251 ^ (rnd.Next(16) == 0 ? rnd.Next(256) : 0));
      var pfsc = MakePfsc(data, i => i % 3 != 2);
      var reader = new PFSCReader(new TestHelper.ArrayMemoryReader(pfsc));
      var got = new byte[data.Length];
      reader.Read(0, got, 0, got.Length);
      CollectionAssert.AreEqual(data, got);
    }
  }
}
