using System;
using System.IO;
using System.Linq;
using LibOrbisPkg.Util;

namespace LibOrbisPkg.PKG
{
  /// <summary>
  /// The application package (pkg_ps4_app) that a cumulative patch applies to,
  /// and what the patch takes from it.
  /// </summary>
  /// <remarks>
  /// A patch names its base package in two ways, and the console checks both when it installs the patch:
  ///   - the base's playgo-chunk.dat, byte for byte, as the patch's app/playgo-chunk.dat entry
  ///     (without it the console refuses the patch with 0x80F00401, or 0x80F00611 at install);
  ///   - the base's ContentDigest and GameDigest in the patch's GENERAL_DIGESTS
  ///     (<see cref="GeneralDigest.OriginDigest"/> and <see cref="GeneralDigest.OriginGameDigest"/>);
  ///     the install fails with 0x80F00611 without them and with 0x80F00612 when they are another package's.
  /// So a patch installs only over the exact package it was built against.
  /// </remarks>
  public class PatchBase
  {
    /// <summary>
    /// The base package's header and SC entries (entries, general digests, param.sfo).
    /// </summary>
    public readonly Pkg Pkg;

    /// <summary>
    /// The base package's playgo-chunk.dat entry, or null if it has none.
    /// </summary>
    public readonly byte[] PlaygoChunkDat;

    public byte[] ContentDigest => Pkg.GeneralDigests.Digests[GeneralDigest.ContentDigest];
    public byte[] GameDigest => Pkg.GeneralDigests.Digests[GeneralDigest.GameDigest];
    public string ContentId => Pkg.Header.content_id;

    private PatchBase(Pkg pkg, byte[] playgoChunkDat)
    {
      Pkg = pkg;
      PlaygoChunkDat = playgoChunkDat;
    }

    /// <summary>
    /// Reads the header and entries of the package at the given path (not its PFS image).
    /// </summary>
    public static PatchBase Read(string path)
    {
      using (var s = File.OpenRead(path))
      {
        return Read(s);
      }
    }

    /// <summary>
    /// Reads the header and entries of the package in the given stream (not its PFS image).
    /// </summary>
    public static PatchBase Read(Stream s)
    {
      var pkg = new PkgReader(s).ReadPkg();
      byte[] chunkDat = null;
      var meta = pkg.Metas.Metas.FirstOrDefault(m => m.id == EntryId.PLAYGO_CHUNK_DAT);
      if (meta != null)
      {
        s.Position = meta.DataOffset;
        chunkDat = s.ReadBytes((int)meta.DataSize);
      }
      return new PatchBase(pkg, chunkDat);
    }

    /// <summary>
    /// Returns why a patch with the given content ID cannot be built against this package, or null if it can.
    /// </summary>
    public string CheckCanPatch(string contentId)
    {
      if (Pkg.Header.content_type != ContentType.GD)
        return $"the base package is not an application package (content type {Pkg.Header.content_type}).";
      if ((Pkg.Header.content_flags & ContentFlags.SUBSEQUENT_PATCH) != 0)
        return "the base package is a patch itself; name the application package that the patch applies to.";
      if (contentId != null && ContentId != contentId)
        return $"the base package's content ID {ContentId} differs from the patch's {contentId}.";
      if (PlaygoChunkDat == null)
        return "the base package has no playgo-chunk.dat entry.";
      if (Pkg.GeneralDigests == null
        || !Pkg.GeneralDigests.set_digests.HasFlag(GeneralDigest.ContentDigest)
        || !Pkg.GeneralDigests.set_digests.HasFlag(GeneralDigest.GameDigest))
        return "the base package's general digests have no Content Digest and Game Digest.";
      return null;
    }
  }
}
