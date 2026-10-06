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
  /// <summary>
  /// Build -> read round trips over the edge cases that real game packages hit:
  /// block boundaries, indirect blocks, huge directories, name-hash collisions, PFSC.
  /// </summary>
  [TestClass]
  public class PfsRoundTripTests
  {
    const int Block = 0x10000;
    const int SigsPerBlock = Block / 36;

    static byte[] Content(string name, long size)
    {
      // Deterministic, mostly incompressible, different per file.
      var data = new byte[size];
      new Random(name.GetHashCode() ^ (int)size).NextBytes(data);
      return data;
    }

    static FSDir MakeTree(IEnumerable<(string path, long size)> files)
    {
      var root = new FSDir();
      foreach (var (path, size) in files)
      {
        var parts = path.Split('/');
        var dir = root;
        foreach (var d in parts.Take(parts.Length - 1))
        {
          var next = dir.Dirs.FirstOrDefault(x => x.name == d);
          if (next == null)
          {
            next = new FSDir { name = d, Parent = dir };
            dir.Dirs.Add(next);
          }
          dir = next;
        }
        var data = Content(path, size);
        dir.Files.Add(new FSFile(s => s.Write(data, 0, data.Length), parts.Last(), size) { Parent = dir });
      }
      return root;
    }

    static PfsProperties InnerProps(FSDir root) => new PfsProperties
    {
      root = root,
      BlockSize = Block,
      Encrypt = false,
      Sign = false,
      FileTime = 1,
    };

    static byte[] BuildImage(PfsProperties props)
    {
      using (var ms = new MemoryStream())
      {
        new PfsBuilder(props).WriteImage(ms);
        return ms.ToArray();
      }
    }

    static byte[] ReadFile(PfsReader.File f)
    {
      var buf = new byte[f.size];
      using (var v = f.GetView())
        v.Read(0, buf, 0, buf.Length);
      return buf;
    }

    static string RelPath(PfsReader.Node n, PfsReader.Dir uroot)
    {
      var parts = new List<string>();
      for (; n != null && n != uroot; n = n.parent)
        parts.Insert(0, n.name);
      return string.Join("/", parts);
    }

    static void AssertNoBlockOverlap(PfsReader reader)
    {
      // Every file (special ones included) must own a disjoint run of blocks.
      IEnumerable<PfsReader.Node> All(PfsReader.Dir d) =>
        new PfsReader.Node[] { d }.Concat(d.children.SelectMany(c => c is PfsReader.Dir cd ? All(cd) : new[] { c }));
      var runs = All(reader.GetSuperRoot())
        .Select(f => (f.name, ino: reader.GetInode(f.ino)))
        .Where(x => x.ino.Blocks > 0)
        .Select(x => (x.name, start: (long)x.ino.StartBlock, end: (long)x.ino.StartBlock + x.ino.Blocks))
        .OrderBy(x => x.start)
        .ToList();
      for (int i = 1; i < runs.Count; i++)
        Assert.IsTrue(runs[i].start >= runs[i - 1].end,
          $"{runs[i - 1].name} [{runs[i - 1].start},{runs[i - 1].end}) overlaps {runs[i].name} at {runs[i].start}");
    }

    static void AssertRoundTrip(IEnumerable<(string path, long size)> files, PfsReader reader)
    {
      var uroot = reader.GetURoot();
      var got = uroot.GetAllFiles().ToDictionary(f => RelPath(f, uroot));
      foreach (var (path, size) in files)
      {
        Assert.IsTrue(got.TryGetValue(path, out var f), $"missing {path}; have [{string.Join(", ", got.Keys.Take(5))}]");
        Assert.AreEqual(size, f.size, $"size of {path}");
        CollectionAssert.AreEqual(Content(path, size), ReadFile(f), $"content of {path}");
      }
      Assert.AreEqual(files.Count(), got.Count, "file count");
    }

    [DataTestMethod]
    [DataRow(0L)]
    [DataRow(1L)]
    [DataRow((long)Block - 1)]
    [DataRow((long)Block)]
    [DataRow((long)Block + 1)]
    [DataRow(12L * Block)]
    [DataRow(12L * Block + 1)]
    [DataRow((12L + SigsPerBlock) * Block + 7)]
    public void InnerPfs_FileSizesAroundBlockBoundaries(long size)
    {
      var files = new[] { ("a.bin", size), ("dir/b.bin", 3L), ("dir/sub/c.bin", size / 2) };
      var img = BuildImage(InnerProps(MakeTree(files)));
      AssertRoundTrip(files, new PfsReader(new TestHelper.ArrayMemoryReader(img)));
    }

    [TestMethod]
    public void OuterPfs_SignedEncrypted_FileNeedsDoublyIndirectBlocks()
    {
      // > 12 direct + one block of indirect pointers -> exercises ib[1] in the signed layout.
      long size = (12L + SigsPerBlock + 5) * Block + 123;
      var files = new[] { ("pfs_image.dat", size) };
      var ekpfs = new byte[32];
      var props = new PfsProperties
      {
        root = MakeTree(files),
        BlockSize = Block,
        Encrypt = true,
        Sign = true,
        EKPFS = ekpfs,
        Seed = new byte[16],
        FileTime = 1,
      };
      using (var ms = new MemoryStream())
      {
        new PfsBuilder(props).WriteImage(ms);
        AssertRoundTrip(files, new PfsReader(new LibOrbisPkg.Util.StreamReader(ms), 0, ekpfs));
      }
    }

    [TestMethod]
    public void InnerPfs_CaseOnlyNameCollisions_SpanMultipleBlocks()
    {
      // PFS path hashes upper-case names, so "x.xxx" and "X.XXX" collide. Thousands of such
      // pairs make both flat_path_table and collision_resolver larger than one block.
      var files = Enumerable.Range(0, 3000)
        .SelectMany(i => new[] { ($"Asset/file_{i:D5}.xxx", 10L + i % 7), ($"Asset/FILE_{i:D5}.XXX", 20L + i % 5) })
        .ToArray();
      var root = MakeTree(files);
      Assert.IsTrue(FlatPathTable.HasCollision(root.GetAllChildren()));
      var img = BuildImage(InnerProps(root));
      var reader = new PfsReader(new TestHelper.ArrayMemoryReader(img));
      AssertNoBlockOverlap(reader);
      AssertRoundTrip(files, reader);
    }

    [TestMethod, TestCategory("Slow")]
    public void InnerPfs_FlatPathTableLargerThan12Blocks_DoesNotOverlapData()
    {
      // 8 bytes per table entry: > 12 * 64 KiB needs ~100k paths (a ~7 GB image, since every file
      // takes a block). Before the fix only the first 12 table blocks were reserved and the rest
      // overlapped the next file's data.
      var files = Enumerable.Range(0, 110000).Select(i => ($"d{i % 50}/f{i:D6}", 1L + i % 3)).ToArray();
      var path = Path.GetTempFileName();
      try
      {
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
        {
          new PfsBuilder(InnerProps(MakeTree(files))).WriteImage(fs);
          var reader = new PfsReader(new LibOrbisPkg.Util.StreamReader(fs));
          var fpt = reader.GetSuperRoot().children.OfType<PfsReader.File>().First(f => f.name == "flat_path_table");
          Assert.IsTrue(reader.GetInode(fpt.ino).Blocks > 12, "test needs a >12 block flat path table");
          AssertNoBlockOverlap(reader);
          AssertRoundTrip(files, reader);
        }
      }
      finally
      {
        File.Delete(path);
      }
    }

    [TestMethod]
    public void InnerPfs_SpecialFilesUseRetailBlockConvention()
    {
      // Retail images: db[0] = start block, db[1..blocks-1] = -1 (contiguous), rest 0.
      var files = Enumerable.Range(0, 3000)
        .SelectMany(i => new[] { ($"Asset/file_{i:D5}.xxx", 1L), ($"Asset/FILE_{i:D5}.XXX", 1L) })
        .ToArray();
      var img = BuildImage(InnerProps(MakeTree(files)));
      var reader = new PfsReader(new TestHelper.ArrayMemoryReader(img));
      var specials = reader.GetSuperRoot().children.OfType<PfsReader.File>().ToList();
      Assert.IsTrue(specials.Any(f => f.name == "collision_resolver"), "expected a collision resolver");
      foreach (var f in specials)
      {
        var ino = reader.GetInode(f.ino);
        Assert.IsTrue(ino.Blocks > 1 || f.name == "flat_path_table", $"{f.name} should span blocks in this test");
        for (int i = 1; i < Math.Min(ino.Blocks, 12); i++)
          Assert.AreEqual(-1, ino.DirectBlocks[i], $"{f.name} db[{i}]");
      }
    }
  }
}
