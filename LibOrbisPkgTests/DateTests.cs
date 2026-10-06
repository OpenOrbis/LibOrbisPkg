using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using LibOrbisPkg.GP4;
using LibOrbisPkg.PKG;

namespace LibOrbisPkgTests
{
  /// <summary>
  /// GP4 dates are written without a zone and are UTC. These only fail on a machine whose local
  /// zone is not UTC (CI usually is UTC), e.g. run with TZ=Europe/Sofia.
  /// </summary>
  [TestClass]
  public class DateTests
  {
    [TestMethod]
    public void VolumeTimestamp_RoundTripsUnchanged()
    {
      var ts = new DateTime(2022, 1, 21, 7, 9, 3, DateTimeKind.Utc);
      var vol = new Volume { TimeStamp = ts };
      Assert.AreEqual(ts, vol.TimeStamp.ToUniversalTime());
      Assert.AreEqual("2022-01-21 07:09:03", vol.volume_ts);
    }

    [TestMethod]
    public void CreationDate_DateOnly_KeepsTheDay()
    {
      var proj = Gp4Project.Create(VolumeType.pkg_ps4_app);
      proj.volume.Package.CreationDate = "2021-08-03";
      var props = PkgProperties.FromGp4(proj, ".");
      Assert.AreEqual("20210803", props.CreationDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
      Assert.IsFalse(props.UseCreationTime);
    }

    [TestMethod]
    public void CreationDate_WithTime_KeepsDateAndTime()
    {
      var proj = Gp4Project.Create(VolumeType.pkg_ps4_app);
      proj.volume.Package.CreationDate = "2022-01-21 08:09:03";
      var props = PkgProperties.FromGp4(proj, ".");
      Assert.AreEqual("20220121 080903", props.CreationDate.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture));
      Assert.IsTrue(props.UseCreationTime);
    }

    [DataTestMethod]
    [DataRow("ar-SA")] // Umm al-Qura calendar: Gregorian 2021 is Hijri 1442
    [DataRow("th-TH")] // Thai Buddhist calendar: 2021 is 2564
    [DataRow("en-US")]
    public void PubToolInfoDate_IsGregorianWhateverTheCulture(string culture)
    {
      var saved = Thread.CurrentThread.CurrentCulture;
      try
      {
        Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
        var props = TestHelper.MakeProperties(VolumeType: VolumeType.pkg_ps4_app,
          CreationDate: new DateTime(2021, 8, 3, 8, 9, 3, DateTimeKind.Utc));
        using (var ms = new MemoryStream())
        {
          new PkgBuilder(props).Write(ms, s => { });
          var pkg = new PkgReader(ms).ReadPkg();
          var info = pkg.ParamSfo.ParamSfo["PUBTOOLINFO"].ToString();
          StringAssert.StartsWith(info, "c_date=20210803,c_time=080903");
        }
      }
      finally
      {
        Thread.CurrentThread.CurrentCulture = saved;
      }
    }
  }
}
