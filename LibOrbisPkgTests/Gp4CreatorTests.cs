using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using LibOrbisPkg.GP4;
using LibOrbisPkg.PFS;
using LibOrbisPkg.PKG;
using LibOrbisPkg.Util;

namespace LibOrbisPkgTests
{
  [TestClass]
  public class Gp4CreatorTests
  {
    /// <summary>
    /// Retail-built packages store nptitle.dat/npbind.dat encrypted (key 3). pkg_makegp4 used to
    /// write the ciphertext out; a rebuilt package then failed to launch on the console with
    /// "invalid nptitle_dat" / CE-33194-0.
    /// </summary>
    [TestMethod]
    public void EncryptedSceSysEntries_AreExtractedDecrypted()
    {
      var nptitle = new byte[0xA0];
      System.Text.Encoding.ASCII.GetBytes("NPTD").CopyTo(nptitle, 0);
      System.Text.Encoding.ASCII.GetBytes("TEST00000_00").CopyTo(nptitle, 0x10);
      // npbind.dat is 0x214 bytes in real packages: not a multiple of the AES block size.
      var npbind = Enumerable.Range(0, 0x214).Select(i => (byte)(i * 7 + 3)).ToArray();
      var props = TestHelper.MakeProperties(VolumeType: VolumeType.pkg_ps4_app,
        sc0Files: new[] {
          new FSFile(s => s.Write(nptitle, 0, nptitle.Length), "nptitle.dat", nptitle.Length),
          new FSFile(s => s.Write(npbind, 0, npbind.Length), "npbind.dat", npbind.Length),
        });

      using (var pkgFile = new TempFile())
      using (var outDir = new TempDir())
      {
        using (var fs = File.Create(pkgFile.Path))
          new PkgBuilder(props).Write(fs, s => { });
        EncryptEntryLikeRetail(pkgFile.Path, EntryId.NPTITLE_DAT, props.ContentId, props.Passcode);
        EncryptEntryLikeRetail(pkgFile.Path, EntryId.NPBIND_DAT, props.ContentId, props.Passcode);

        Gp4Creator.CreateProjectFromPKG(outDir.Path, pkgFile.Path);
        CollectionAssert.AreEqual(nptitle, File.ReadAllBytes(Path.Combine(outDir.Path, "sce_sys", "nptitle.dat")));
        CollectionAssert.AreEqual(npbind, File.ReadAllBytes(Path.Combine(outDir.Path, "sce_sys", "npbind.dat")));

        // The package digest of an encrypted entry covers the padded ciphertext.
        using (var fs = File.OpenRead(pkgFile.Path))
        {
          var pkg = new PkgReader(fs).ReadPkg();
          var bad = new PkgValidator(pkg).Validate(fs)
            .Where(v => v.Item1.Name.Contains("NPBIND") || v.Item1.Name.Contains("NPTITLE"))
            .Where(v => v.Item2 != PkgValidator.ValidationResult.Ok).Select(v => v.Item1.Name).ToList();
          Assert.AreEqual(0, bad.Count, string.Join(", ", bad));
        }
      }
    }

    /// <summary>
    /// Flags the entry as encrypted with key 3 and encrypts it in place like retail packages:
    /// AES-CBC over the plaintext zero-padded to 16 bytes (DataSize keeps the plaintext length),
    /// and the entry digest is updated to the hash of the padded ciphertext.
    /// </summary>
    static void EncryptEntryLikeRetail(string path, EntryId id, string contentId, string passcode)
    {
      Pkg pkg;
      using (var fs = File.OpenRead(path))
        pkg = new PkgReader(fs).ReadPkg();
      int index = pkg.Metas.Metas.FindIndex(m => m.id == id);
      var meta = pkg.Metas.Metas[index];
      meta.Flags1 |= 0x80000000;
      meta.Flags2 = (meta.Flags2 & ~0xF000u) | (3u << 12);

      var iv_key = Crypto.Sha256(meta.GetBytes().Concat(Crypto.ComputeKeys(contentId, passcode, 3)).ToArray());
      using (var fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
      {
        var data = new byte[(meta.DataSize + 15) & ~15u];
        fs.Position = meta.DataOffset;
        fs.ReadExactly(data, 0, (int)meta.DataSize);
        Crypto.AesCbcCfb128Encrypt(data, data, data.Length, iv_key.Skip(16).Take(16).ToArray(), iv_key.Take(16).ToArray());
        fs.Position = meta.DataOffset;
        fs.Write(data, 0, data.Length);
        fs.Position = pkg.Header.entry_table_offset + index * 32;
        meta.Write(fs);
        var digests = pkg.Metas.Metas.First(m => m.id == EntryId.DIGESTS);
        fs.Position = digests.DataOffset + 32 * index;
        fs.Write(Crypto.Sha256(data), 0, 32);
      }
    }
  }
}
